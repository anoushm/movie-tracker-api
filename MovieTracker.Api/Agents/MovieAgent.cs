using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Telemetry;
using MovieTracker.Api.Tools;
using TelemetryHelpers = MovieTracker.Api.Telemetry.Telemetry;

namespace MovieTracker.Api.Agents;

public static class MovieAgent
{
    public static void AddMovieAgent(
        this IServiceCollection services,
        Uri openAiEndpoint,
        string openAiApiKey,
        string openAiDeploymentName,
        bool enableSensitiveTelemetry)
    {
        services.AddScoped<IChatClient>(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = httpClientFactory.CreateClient("HttpClient");
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            AzureOpenAIClientOptions clientOptions = new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClient)
            };

            AzureOpenAIClient azureOpenAiClient = new AzureOpenAIClient(
                openAiEndpoint,
                new ApiKeyCredential(openAiApiKey),
                clientOptions);

            return azureOpenAiClient
                .GetChatClient(openAiDeploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation(loggerFactory, client =>
                {
                    client.MaximumIterationsPerRequest = 12;
                })
                .UseOpenTelemetry(loggerFactory, TelemetryHelpers.SourceName, options => options.EnableSensitiveData = enableSensitiveTelemetry)
                .Build(sp);
        });

        services.AddScoped<AIAgent>(sp =>
        {
            IChatClient chatClient = sp.GetRequiredService<IChatClient>();
            TmdbTools tmdbTools = sp.GetRequiredService<TmdbTools>();
            ChatPlanner chatPlanner = sp.GetRequiredService<ChatPlanner>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            List<AITool> tools =
            [
                .. tmdbTools.CreateTools(),
                .. DateTimeTools.CreateTools(),
                .. chatPlanner.CreateTools(),
            ];

            ILogger toolLogger = loggerFactory.CreateLogger("MovieTracker.Api.ToolCalls");
            tools = [.. tools.Select(tool =>
                tool is AIFunction function
                    ? new InstrumentedAIFunction(function, toolLogger, enableSensitiveTelemetry)
                    : tool)];

            return chatClient.AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "MovieTracker.Api",
                    ChatOptions = new ChatOptions { Tools = tools }
                },
                loggerFactory,
                sp);
        });
    }
}
