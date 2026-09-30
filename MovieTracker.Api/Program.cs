using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using MovieTracker.Api.Core;
using MovieTracker.Api.Tools;
using OpenAI.Chat;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IConfigurationSection config = builder.Configuration.GetSection("AzureOpenAI");
Uri endpoint = new Uri(config["Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:Endpoint"));
AzureKeyCredential apiKey = new AzureKeyCredential(config["ApiKey"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:ApiKey"));
string deploymentName = config["DeploymentName"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:DeploymentName");

AIAgent movieAgent = new AzureOpenAIClient(endpoint, apiKey)
    .GetChatClient(deploymentName)
    .AsAIAgent(
        instructions: "You are a helpful assistant in movie tracking.",
        name: "Movie Assistant");

AIAgent dateTimeAgent = new AzureOpenAIClient(endpoint, apiKey)
    .GetChatClient(deploymentName)
    .AsAIAgent(
        instructions: "You answer questions about the date or time.",
        name: "DateTime Agent",
        description: "An agent that answers date time, period, moment, and duration.",
        tools: [
            AIFunctionFactory.Create((Func<string>)DateTimeTool.Today),
            AIFunctionFactory.Create((Func<string>)DateTimeTool.ThisMonth),
            AIFunctionFactory.Create((Func<string>)DateTimeTool.ThisYear),
            AIFunctionFactory.Create((Func<int, string>)DateTimeTool.PastYearsRange),
            AIFunctionFactory.Create((Func<int, string>)DateTimeTool.PastMonthsRange),
            AIFunctionFactory.Create((Func<int, string>)DateTimeTool.PastDaysRange),
            AIFunctionFactory.Create((Func<string, int, string, string>)DateTimeTool.OffsetDate)
        ]);

builder.Services.AddSingleton(movieAgent);
builder.Services.AddSingleton(dateTimeAgent);
builder.Services.AddSingleton<TheMovieDBTool>();

builder.Services.AddSingleton<AIAgent>(sp =>
{
    TheMovieDBTool theMovieDbTool = sp.GetRequiredService<TheMovieDBTool>();

    return new AzureOpenAIClient(endpoint, apiKey)
        .GetChatClient(deploymentName)
        .AsAIAgent(
            instructions: "You are a movie data assistant that answers using live TMDb data.",
            name: "TheMovieDb Agent",
            description: "Provides movie details, search, trailers, genres, keywords, and discovery via TMDb.",
            tools: [
                AIFunctionFactory.Create(() => theMovieDbTool.GetGenresList().UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string personName) => theMovieDbTool.SearchForPeople(personName).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string movieTitle, string? releaseYear) => theMovieDbTool.SearchMovies(movieTitle, releaseYear).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string movieId) => theMovieDbTool.GetMovieTrailers(movieId).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string movieId) => theMovieDbTool.GetMovieWithTrailer(movieId).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string userQuery) => theMovieDbTool.HandleGenericTrailerRequest(userQuery).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string movieId) => theMovieDbTool.GetMovieDetails(movieId).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string keyword) => theMovieDbTool.SearchKeywords(keyword).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string movieId) => theMovieDbTool.DescribeMovie(movieId).UnwrapForAgentAsync()),
                AIFunctionFactory.Create((string? releaseDateFrom, string? releaseDateTo, string? castIds, string? genreIds, string? keywordIds, double? minVoteAverage, double? maxVoteAverage, int? minVoteCount, int? maxVoteCount) =>
                    theMovieDbTool.DiscoverMovies(releaseDateFrom, releaseDateTo, castIds, genreIds, keywordIds, minVoteAverage, maxVoteAverage, minVoteCount, maxVoteCount).UnwrapForAgentAsync())
            ]);
});

string cosmosEndpoint = builder.Configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Endpoint");
string cosmosDatabase = builder.Configuration["Cosmos:Database"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Database");
string cosmosContainer = builder.Configuration["Cosmos:Container"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Container");

builder.Services.AddSingleton(sp => new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), new CosmosClientOptions { Serializer = new CosmosSystemTextJsonSerializer() }));

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
