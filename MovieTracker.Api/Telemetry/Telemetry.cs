using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;

namespace MovieTracker.Api.Telemetry;

internal static class Telemetry
{
    public const string SourceName = "movie-tracker-api";

    public const string ServiceVersion = "1.0.0";

    private static readonly Meter Meter = new(SourceName, ServiceVersion);

    public static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        "gen_ai.client.tool.duration",
        unit: "s",
        description: "Wall-clock duration of a single tool invocation.");

    public static readonly Counter<long> ToolInvocations = Meter.CreateCounter<long>(
        "gen_ai.client.tool.invocations",
        unit: "{invocation}",
        description: "Tool invocations, dimensioned by tool name and outcome.");

    public static readonly Histogram<int> ToolCallsPerTurn = Meter.CreateHistogram<int>(
        "chat.turn.tool_calls",
        unit: "{call}",
        description: "Tool calls issued while answering one user turn.");

    public static readonly Histogram<double> ContextRetainedRatio = Meter.CreateHistogram<double>(
        "chat.context.retained_ratio",
        unit: "1",
        description: "Messages sent to the model divided by messages stored for the session.");

    public static readonly Counter<double> CostUsd = Meter.CreateCounter<double>(
        "gen_ai.usage.cost_usd",
        unit: "USD",
        description: "Estimated spend, derived from token counts and configured per-1M rates.");

    public static void RecordTool(string toolName, TimeSpan elapsed, bool succeeded)
    {
        KeyValuePair<string, object?> tool = new KeyValuePair<string, object?>("gen_ai.tool.name", toolName);
        KeyValuePair<string, object?> outcome = new KeyValuePair<string, object?>("outcome", succeeded ? "ok" : "error");

        ToolDuration.Record(elapsed.TotalSeconds, tool, outcome);
        ToolInvocations.Add(1, tool, outcome);
    }

    public static void RecordCost(string model, UsageDetails usage, ModelPricing pricing)
    {
        long cachedInput = usage.CachedInputTokenCount ?? 0;
        long freshInput = Math.Max(0, (usage.InputTokenCount ?? 0) - cachedInput);

        double cost = freshInput / 1_000_000d * pricing.InputPerMillion
                    + cachedInput / 1_000_000d * (pricing.CachedInputPerMillion ?? pricing.InputPerMillion)
                    + (usage.OutputTokenCount ?? 0) / 1_000_000d * pricing.OutputPerMillion;

        if (cost > 0)
        {
            CostUsd.Add(cost, new KeyValuePair<string, object?>("gen_ai.request.model", model));
        }
    }
}
