using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using StockScout.Core.Models;

namespace StockScout.Core.Ai;

public interface IStockAnalyst
{
    Task<AnalystResult> AnalyzeAsync(IReadOnlyList<Candidate> shortlist, DateTimeOffset asOf, CancellationToken ct);
}

/// <summary>Thrown for problems the user can act on (missing key, refusal, rate limit...).</summary>
public sealed class AnalystException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ClaudeAnalystOptions
{
    public string Model { get; init; } = "claude-sonnet-5-5";
    public int MaxTokens { get; init; } = 16000;
}

/// <summary>Sends the shortlist to Claude and gets back structured, validated picks.</summary>
public sealed class ClaudeStockAnalyst(ClaudeAnalystOptions options, AnthropicClient? client = null) : IStockAnalyst
{
    // USD per million tokens (input, output) - used only for the cost estimate shown in the UI.
    private static readonly Dictionary<string, (decimal In, decimal Out)> Pricing = new()
    {
        ["claude-sonnet-5-5"] = (2m, 10m),
        ["claude-opus-5-5"] = (4m, 20m),
        ["claude-haiku-4-5"] = (1m, 5m),
    };

    // Created lazily so the app can start (and show a helpful message) without an API key.
    private AnthropicClient? _client = client;

    public async Task<AnalystResult> AnalyzeAsync(IReadOnlyList<Candidate> shortlist, DateTimeOffset asOf, CancellationToken ct)
    {
        if (shortlist.Count == 0)
            throw new AnalystException("No stocks passed the screen, so there is nothing to analyse.");

        _client ??= new AnthropicClient();

        BetaMessage response;
        try
        {
            response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                // If a safety classifier declines the request, the API retries on a suitable fallback model in the same call.
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(), // serialises as "default"
                System = AnalystPrompt.SystemPrompt,
                OutputConfig = new BetaOutputConfig
                {
                    Format = new BetaJsonOutputFormat { Schema = AnalystPrompt.OutputSchema() },
                },
                Messages = [new BetaMessageParam { Role = Role.User, Content = AnalystPrompt.BuildUserMessage(shortlist, asOf) }],
            }, ct);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AnalystException("Claude rejected the API key. Check the ANTHROPIC_API_KEY environment variable.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AnalystException("Claude rate limit hit. Wait a minute and try again.", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new AnalystException($"Claude API error: {ex.Message}", ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new AnalystException("Could not reach the Claude API. Check your internet connection.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsMissingCredentials(ex))
        {
            throw new AnalystException(
                "No Anthropic API key found. Create one at console.anthropic.com, then set the ANTHROPIC_API_KEY " +
                "environment variable and restart the app.", ex);
        }

        if (response.StopReason == "refusal")
            throw new AnalystException("Claude declined to answer this request.");
        if (response.StopReason == "max_tokens")
            throw new AnalystException("Claude's answer was cut off (max_tokens). Try again or raise MaxTokens.");

        var json = string.Concat(response.Content
            .Select(b => b.TryPickText(out var text) ? text.Text : null)
            .Where(t => t is not null));

        var (summary, picks, warnings) = AnalystResponseParser.Parse(json, shortlist);

        long inTokens = response.Usage.InputTokens;
        long outTokens = response.Usage.OutputTokens;
        (decimal In, decimal Out) price = Pricing.GetValueOrDefault(options.Model, (2m, 10m));
        decimal cost = (inTokens * price.In + outTokens * price.Out) / 1_000_000m;

        return new AnalystResult(summary, picks, warnings, response.Model, inTokens, outTokens, cost);
    }

    private static bool IsMissingCredentials(Exception ex) =>
        ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase);
}
