using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using MovieTracker.Api.Agents;
using MovieTracker.Api.Chat;
using MovieTracker.Api.ChatSessions;
using MovieTracker.Api.Core;
using MovieTracker.Api.Prompts;
using MovieTracker.Api.Telemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TMDbLib.Client;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using TelemetryHelpers = MovieTracker.Api.Telemetry.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IConfigurationSection config = builder.Configuration.GetSection("AzureOpenAI");
Uri endpoint = new Uri(config["Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:Endpoint"));
string apiKey = config["ApiKey"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:ApiKey");
string deploymentName = config["DeploymentName"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:DeploymentName");

string tmdbApiKey = builder.Configuration["TheMovieDb:Api-Key"] ?? throw new InvalidOperationException("Missing configuration value: TheMovieDb:Api-Key");

string cosmosEndpoint = builder.Configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Endpoint");
string cosmosDatabase = builder.Configuration["Cosmos:Database"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Database");
string cosmosContainer = builder.Configuration["Cosmos:Container"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Container");

bool enableSensitiveTelemetry = builder.Configuration.GetValue<bool?>("Telemetry:Enable-Sensitive-Data") ?? true;

ModelPricing? modelPricing = null;
if (double.TryParse(builder.Configuration["AzureOpenAI:Price-Input-Per-1M"], out double inputRate)
    && double.TryParse(builder.Configuration["AzureOpenAI:Price-Output-Per-1M"], out double outputRate))
{
    double? cachedRate = double.TryParse(builder.Configuration["AzureOpenAI:Price-Cached-Input-Per-1M"], out double parsedCached)
        ? parsedCached
        : null;
    modelPricing = new ModelPricing(inputRate, outputRate, cachedRate);
}
builder.Services.AddSingleton<ModelPricing?>(_ => modelPricing);

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

builder.Services.AddDistributedMemoryCache();

builder.Services.AddHttpClient("HttpClient")
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
        options.Retry.MaxRetryAttempts = 3;
    });

builder.Services.AddSingleton<TMDbClient>(sp => new TMDbClient(tmdbApiKey));
builder.Services.AddSingleton<TheMovieDBKernelFunctions>();

builder.Services.AddHttpClient<WikipediaSearchAgent>(httpClient =>
{
    string serviceName = typeof(Program).Assembly.GetName().Name ?? "MovieTracker.Api";
    string serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    httpClient.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue(serviceName, serviceVersion));
    httpClient.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("(+https://github.com/anoushm/movie-tracker-api)"));

    httpClient.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddScoped<OpenMovieDbAgent>();
builder.Services.AddScoped<TrailerAgent>();
builder.Services.AddScoped<ChatPlanner>();

builder.Services.AddScoped<IChatClient>(sp =>
{
    IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    HttpClient httpClient = httpClientFactory.CreateClient("HttpClient");
    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    AzureOpenAIClientOptions clientOptions = new AzureOpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient)
    };

    AzureOpenAIClient azureOpenAiClient = new AzureOpenAIClient(
        endpoint,
        new ApiKeyCredential(apiKey),
        clientOptions);

    return azureOpenAiClient
        .GetChatClient(deploymentName)
        .AsIChatClient()
        .AsBuilder()
        .UseFunctionInvocation(loggerFactory, client =>
        {
            client.MaximumIterationsPerRequest = 12;
        })
        .UseOpenTelemetry(loggerFactory, TelemetryHelpers.SourceName, options => options.EnableSensitiveData = enableSensitiveTelemetry)
        .Build(sp);
});

builder.Services.AddScoped<AIAgent>(sp =>
{
    IChatClient chatClient = sp.GetRequiredService<IChatClient>();
    TheMovieDBKernelFunctions tmdbFunctions = sp.GetRequiredService<TheMovieDBKernelFunctions>();
    ChatPlanner chatPlanner = sp.GetRequiredService<ChatPlanner>();
    ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    List<AITool> tools =
    [
        .. tmdbFunctions.CreateTools(),
        .. DateTimeKernelFunctions.CreateTools(),
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

builder.Services.AddSingleton(sp => new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), new CosmosClientOptions { Serializer = new CosmosSystemTextJsonSerializer() }));

builder.Services.AddSingleton(sp => new ChatSessionRepository(
    sp.GetRequiredService<CosmosClient>(),
    cosmosDatabase,
    cosmosContainer,
    sp.GetRequiredService<Tracer>(),
    sp.GetRequiredService<ILogger<ChatSessionRepository>>()));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Container"))
{
    app.UseHttpsRedirection();
}

app.MapControllers();

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

app.MapGet("/api/chat/start", async (ChatSessionRepository repo, Tracer tracer, ILogger<Program> logger) =>
{
    using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-start");
    try
    {
        List<AIChatMessage> initialHistory = new List<AIChatMessage>
        {
            new AIChatMessage(ChatRole.System, ChatStartSystemPrompt.Text)
        };
        MoviceTrackerChatSession session = await repo.NewChatSession(initialHistory);
        return Results.Ok(new { chatId = session.id });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Chat-Start session creation failed.");
        return Results.Json(new { error = "Session creation failed." }, statusCode: 503);
    }
})
    .WithName("ChatStart");

app.MapPost("/api/chat/{chatId}/ask", ChatTurnEndpoints.Ask)
    .WithName("ChatAsk");

app.MapPost("/api/chat/{chatId}/ask/v2", ChatTurnEndpoints.AskV2)
    .WithName("ChatAskV2");

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
        AssemblyVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
        Revision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION") ?? "local",
        Replica = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? Environment.MachineName,
        Environment = app.Environment.EnvironmentName,
        Framework = RuntimeInformation.FrameworkDescription,
        StartedAt = processStartedAt.ToString("o"),
        Uptime = uptime.ToString("c")
    };
});

app.Run();
