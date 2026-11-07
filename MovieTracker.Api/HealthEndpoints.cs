using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace MovieTracker.Api;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok("healthy"))
            .WithName("Health");

        app.MapGet("/health/ready", () => Results.Ok("ready"))
            .WithName("Ready");

        app.MapGet("/health/cosmos", async (CosmosClient cosmosClient, IConfiguration configuration) =>
        {
            string database = configuration["Cosmos:Database"]!;
            string container = configuration["Cosmos:Container"]!;
            string account = new Uri(configuration["Cosmos:Endpoint"]!).Host;

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

            try
            {
                Container cosmosContainerClient = cosmosClient.GetContainer(database, container);
                await cosmosContainerClient.ReadContainerAsync(cancellationToken: cts.Token);
                return Results.Ok(new { status = "ok", account, database, container });
            }
            catch (CosmosException ex)
            {
                string reason = ex.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authorization",
                    HttpStatusCode.NotFound => "not-found",
                    _ => "unavailable"
                };
                object body = reason == "authorization"
                    ? new { status = "fail", reason, account, database, container, requiredRole = "Cosmos DB Built-in Data Contributor" }
                    : (object)new { status = "fail", reason, account, database, container };
                return Results.Json(body, statusCode: 503);
            }
            catch (AuthenticationFailedException)
            {
                return Results.Json(
                    new { status = "fail", reason = "authorization", account, database, container, requiredRole = "Cosmos DB Built-in Data Contributor" },
                    statusCode: 503);
            }
            catch (OperationCanceledException)
            {
                return Results.Json(
                    new { status = "fail", reason = "timeout", account, database, container },
                    statusCode: 503);
            }
        })
            .WithName("CosmosHealth");

        app.MapGet("/health/tmdb", async (TMDbClient tmdbClient, ILogger<Program> logger) =>
        {
            try
            {
                await tmdbClient.GetConfigAsync();
                return Results.Ok(new { status = "ok", baseUrl = "https://api.themoviedb.org/3" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TMDb probe failed");
                return Results.Json(new { status = "fail", reason = "unavailable" }, statusCode: 503);
            }
        })
            .WithName("TmdbHealth");

        app.MapGet("/health/openai", async (IChatClient chatClient, IConfiguration configuration, ILogger<Program> logger) =>
        {
            string endpointHost = new Uri(configuration["AzureOpenAI:Endpoint"]!).Host;
            string deployment = configuration["AzureOpenAI:DeploymentName"]!;

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await chatClient.GetResponseAsync(
                    [new AIChatMessage(ChatRole.User, "ping")],
                    new ChatOptions { MaxOutputTokens = 1 },
                    cts.Token);
                return Results.Ok(new { status = "ok", endpoint = endpointHost, deployment });
            }
            catch (OperationCanceledException)
            {
                return Results.Json(
                    new { status = "fail", reason = "timeout", endpoint = endpointHost, deployment },
                    statusCode: 503);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenAI probe failed");
                return Results.Json(
                    new { status = "fail", reason = "unavailable", endpoint = endpointHost, deployment },
                    statusCode: 503);
            }
        })
            .WithName("OpenAiHealth");

        app.MapGet("/version", () =>
        {
            using Process currentProcess = Process.GetCurrentProcess();
            DateTime processStartedAt = currentProcess.StartTime.ToUniversalTime();
            TimeSpan uptime = DateTime.UtcNow - processStartedAt;
            return new
            {
                ImageTag = Environment.GetEnvironmentVariable("IMAGE_TAG") ?? "unknown",
                GitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "unknown",
                BuildTime = Environment.GetEnvironmentVariable("BUILD_TIME") ?? "unknown",
                AssemblyVersion = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString(),
                Revision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION") ?? "local",
                Replica = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? Environment.MachineName,
                Environment = app.Environment.EnvironmentName,
                Framework = RuntimeInformation.FrameworkDescription,
                StartedAt = processStartedAt.ToString("o"),
                Uptime = uptime.ToString("c")
            };
        });
    }
}
