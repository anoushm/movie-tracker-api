using System;
using Azure.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieTracker.Api;
using MovieTracker.Api.Agents;
using MovieTracker.Api.Azure;
using MovieTracker.Api.Chat;
using MovieTracker.Api.ChatSessions;
using MovieTracker.Api.Telemetry;
using MovieTracker.Api.Tools;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

AzureCredentialFactory credentialFactory = new AzureCredentialFactory(builder.Configuration);
TokenCredential credential = credentialFactory.Create();
builder.AddKeyVaultWhenLocal(credentialFactory, credential);

IConfiguration configuration = builder.Configuration;

Uri openAiEndpoint = new Uri(configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:Endpoint"));
string openAiApiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:ApiKey");
string openAiDeploymentName = configuration["AzureOpenAI:DeploymentName"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:DeploymentName");
string tmdbApiKey = configuration["TheMovieDb:Api-Key"] ?? throw new InvalidOperationException("Missing configuration value: TheMovieDb:Api-Key");
string cosmosEndpoint = configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Endpoint");
string cosmosDatabase = configuration["Cosmos:Database"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Database");
string cosmosContainer = configuration["Cosmos:Container"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Container");
bool enableSensitiveTelemetry = configuration.GetValue<bool?>("Telemetry:Enable-Sensitive-Data") ?? true;

builder.AddTelemetry();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMovieTools(tmdbApiKey);
builder.Services.AddMovieAgent(openAiEndpoint, openAiApiKey, openAiDeploymentName, enableSensitiveTelemetry);
builder.Services.AddChatSessions(cosmosEndpoint, cosmosDatabase, cosmosContainer, credential);
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

app.MapHealthEndpoints();
app.MapChatEndpoints();

app.Run();

