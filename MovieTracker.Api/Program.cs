using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MovieTracker.Api.Tools;
using System.ComponentModel;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration.GetSection("AzureOpenAI");
var endpoint = new Uri(config["Endpoint"]!);
var apiKey = new AzureKeyCredential(config["ApiKey"]!);
var deploymentName = config["DeploymentName"]!;
var agentName = config["AgentName"]!;

AIAgent movieAgent = new AzureOpenAIClient(endpoint, apiKey)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .CreateAIAgent(
    instructions: "You are a helpful assistant in movie tracking.", name: "Movie Assistant");

AIAgent dateTimeAgent = new AzureOpenAIClient(endpoint, apiKey)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .CreateAIAgent(
        instructions: "You answer questions about the date or time.",
        name: "DateTime Agent",
        description: "An agent that answers date time, peroid, moment, and duration.",
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

var theMovieDbTool = new TheMovieDBTool(builder.Configuration);

AIAgent theMovieDbAgent = new AzureOpenAIClient(endpoint, apiKey)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .CreateAIAgent(
        instructions: "You are a movie data assistant that answers using live TMDb data.",
        name: "TheMovieDb Agent",
        description: "Provides movie details, search, trailers, genres, keywords, and discovery via TMDb.",
        tools: [
            AIFunctionFactory.Create((Func<Task<string>>)theMovieDbTool.GetGenresList),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.SearchForPeople),
            AIFunctionFactory.Create((Func<string, string?, Task<string>>)theMovieDbTool.SearchMovies),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.GetMovieTrailers),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.GetMovieWithTrailer),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.HandleGenericTrailerRequest),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.GetMovieDetails),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.SearchKeywords),
            AIFunctionFactory.Create((Func<string, Task<string>>)theMovieDbTool.DescribeMovie),
            AIFunctionFactory.Create((Func<string?, string?, string?, string?, string?, double?, double?, int?, int?, Task<string>>)theMovieDbTool.DiscoverMovies)
        ]);

builder.Services.AddSingleton(theMovieDbTool);
builder.Services.AddSingleton(theMovieDbAgent);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Only use HTTPS redirection when not in a container
if (app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Container"))
{
    app.UseHttpsRedirection();
}

app.MapControllers();

// Health check endpoints
app.MapGet("/health", () => Results.Ok("healthy"))
    .WithName("Health")
    .WithOpenApi();

app.MapGet("/health/ready", () => Results.Ok("ready"))
    .WithName("Ready")
    .WithOpenApi();

app.MapGet("/version", () => new
{
    Version = typeof(Program).Assembly.GetName().Version?.ToString(),
    BuildTime = File.GetLastWriteTimeUtc(typeof(Program).Assembly.Location).ToString("o"),
    Environment = app.Environment.EnvironmentName,
    MachineName = Environment.MachineName
});

app.Run();
