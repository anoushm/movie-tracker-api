using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using TMDbLib.Client;

namespace MovieTracker.Api.Tools;

public static class MovieTools
{
    public static void AddMovieTools(this IServiceCollection services, string tmdbApiKey)
    {
        services.AddHttpClient("HttpClient")
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
                options.Retry.MaxRetryAttempts = 3;
            });

        services.AddSingleton<TMDbClient>(sp => new TMDbClient(tmdbApiKey));
        services.AddSingleton<TmdbTools>();

        services.AddHttpClient<WikipediaTools>(httpClient =>
        {
            string serviceName = typeof(MovieTools).Assembly.GetName().Name ?? "MovieTracker.Api";
            string serviceVersion = typeof(MovieTools).Assembly.GetName().Version?.ToString() ?? "0.0.0";

            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(serviceName, serviceVersion));
            httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("(+https://github.com/anoushm/movie-tracker-api)"));

            httpClient.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<OmdbTools>();
        services.AddScoped<TrailerTools>();
        services.AddScoped<ChatPlanner>();
    }
}
