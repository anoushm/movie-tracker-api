using System;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Telemetry;
using MovieTracker.Api.Tools;

namespace MovieTracker.Api.Chat;

public static class ChatPipelineRegistration
{
    public static void AddChatPipeline(this IServiceCollection services)
    {
        services.AddSingleton<ChatContextReducer>();
        services.AddSingleton<UsageLogger>(sp => new UsageLogger(
            sp.GetRequiredService<ILogger<UsageLogger>>(),
            sp.GetService<ModelPricing>()));
        services.AddScoped<MovieHydrator>();
        services.AddScoped<Lazy<AIAgent>>(sp => new Lazy<AIAgent>(sp.GetRequiredService<AIAgent>));
        services.AddScoped<Lazy<ChatPlanner>>(sp => new Lazy<ChatPlanner>(sp.GetRequiredService<ChatPlanner>));
        services.AddScoped<Lazy<MovieHydrator>>(sp => new Lazy<MovieHydrator>(sp.GetRequiredService<MovieHydrator>));
        services.AddScoped<IChatPipeline, ChatPipeline>();
    }
}
