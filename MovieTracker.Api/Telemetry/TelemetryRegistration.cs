using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TelemetryHelpers = MovieTracker.Api.Telemetry.Telemetry;

namespace MovieTracker.Api.Telemetry;

public static class TelemetryRegistration
{
    public static void AddTelemetry(this WebApplicationBuilder builder)
    {
        if (double.TryParse(builder.Configuration["AzureOpenAI:Price-Input-Per-1M"], out double inputRate)
            && double.TryParse(builder.Configuration["AzureOpenAI:Price-Output-Per-1M"], out double outputRate))
        {
            double? cachedRate = double.TryParse(builder.Configuration["AzureOpenAI:Price-Cached-Input-Per-1M"], out double parsedCached)
                ? parsedCached
                : null;
            builder.Services.AddSingleton(new ModelPricing(inputRate, outputRate, cachedRate));
        }

        builder.Services.AddSingleton(TracerProvider.Default.GetTracer(TelemetryHelpers.SourceName, TelemetryHelpers.ServiceVersion));

        string? appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        ResourceBuilder telemetryResource = ResourceBuilder.CreateDefault()
            .AddService(TelemetryHelpers.SourceName, TelemetryHelpers.ServiceVersion);

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(telemetryResource);
            options.AddAzureMonitorLogExporter(exporter =>
            {
                exporter.ConnectionString = appInsightsConnectionString;
                exporter.EnableTraceBasedLogsSampler = false;
            });
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(TelemetryHelpers.SourceName)
                    .AddSource("Microsoft.Extensions.AI*")
                    .AddSource("Microsoft.Agents.AI*")
                    .AddSource("Azure.AI.OpenAI*")
                    .AddSource("OpenAI*")
                    .SetResourceBuilder(telemetryResource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAzureMonitorTraceExporter(options => options.ConnectionString = appInsightsConnectionString);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(TelemetryHelpers.SourceName)
                    .SetResourceBuilder(telemetryResource)
                    .AddHttpClientInstrumentation()
                    .AddAzureMonitorMetricExporter(options => options.ConnectionString = appInsightsConnectionString);
            });
    }
}
