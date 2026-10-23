using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MovieTracker.Api.Telemetry;

internal sealed class InstrumentedAIFunction(
    AIFunction inner,
    ILogger? logger = null,
    bool logArguments = false) : DelegatingAIFunction(inner)
{
    private const int MaxLoggedArgumentChars = 120;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            object? result = await base.InvokeCoreAsync(arguments, cancellationToken);
            Telemetry.RecordTool(Name, stopwatch.Elapsed, succeeded: true);
            LogCall(arguments, stopwatch.Elapsed, error: null);
            return result;
        }
        catch (Exception ex)
        {
            Telemetry.RecordTool(Name, stopwatch.Elapsed, succeeded: false);
            LogCall(arguments, stopwatch.Elapsed, ex);
            throw;
        }
    }

    private void LogCall(AIFunctionArguments arguments, TimeSpan elapsed, Exception? error)
    {
        if (logger is null)
        {
            return;
        }

        LogLevel level = error is null ? LogLevel.Information : LogLevel.Warning;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        string rendered = logArguments ? Render(arguments) : "(arguments not logged)";

        if (error is null)
        {
            logger.LogInformation("tool {ToolName}({ToolArguments}) ok in {ElapsedMs}ms",
                Name, rendered, (int)elapsed.TotalMilliseconds);
        }
        else
        {
            logger.LogWarning("tool {ToolName}({ToolArguments}) FAILED in {ElapsedMs}ms: {Error}",
                Name, rendered, (int)elapsed.TotalMilliseconds, error.Message);
        }
    }

    private string Render(AIFunctionArguments arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", arguments
            .Where(argument => argument.Value is not null)
            .Select(argument =>
            {
                string value = argument.Value!.ToString() ?? string.Empty;
                if (value.Length > MaxLoggedArgumentChars)
                {
                    value = value[..MaxLoggedArgumentChars] + "...";
                }
                return $"{argument.Key}={value}";
            }));
    }
}
