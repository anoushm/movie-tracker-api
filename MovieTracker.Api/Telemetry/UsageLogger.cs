using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using TelemetryHelpers = MovieTracker.Api.Telemetry.Telemetry;

namespace MovieTracker.Api.Telemetry;

public sealed class UsageLogger
{
    private readonly ILogger<UsageLogger> logger;
    private readonly ModelPricing? modelPricing;

    public UsageLogger(ILogger<UsageLogger> logger, ModelPricing? modelPricing)
    {
        this.logger = logger;
        this.modelPricing = modelPricing;
    }

    public void LogAndTag(AgentResponse result, string deployedModel)
    {
        if (result.Usage is not { } usage)
        {
            return;
        }

        this.logger.LogInformation(
            "Chat-Ask usage: input={InputTokens} cachedInput={CachedInputTokens} output={OutputTokens} reasoning={ReasoningTokens} total={TotalTokens}",
            usage.InputTokenCount,
            usage.CachedInputTokenCount,
            usage.OutputTokenCount,
            usage.ReasoningTokenCount,
            usage.TotalTokenCount);

        Activity? activity = Activity.Current;
        if (activity != null)
        {
            if (usage.InputTokenCount is { } inputTokens)
            {
                activity.SetTag("gen_ai.usage.input_tokens", inputTokens);
            }
            if (usage.OutputTokenCount is { } outputTokens)
            {
                activity.SetTag("gen_ai.usage.output_tokens", outputTokens);
            }
            if (usage.CachedInputTokenCount is { } cachedInputTokens)
            {
                activity.SetTag("gen_ai.usage.cache_read.input_tokens", cachedInputTokens);
            }
            if (usage.ReasoningTokenCount is { } reasoningTokens)
            {
                activity.SetTag("gen_ai.usage.reasoning.output_tokens", reasoningTokens);
            }
        }

        if (this.modelPricing is { } pricing)
        {
            TelemetryHelpers.RecordCost(deployedModel, usage, pricing);
        }
    }
}
