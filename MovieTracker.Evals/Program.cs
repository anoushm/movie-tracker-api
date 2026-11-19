using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MovieTracker.Evals.Agent;
using MovieTracker.Evals.Cases;
using MovieTracker.Evals.Checks;
using MovieTracker.Evals.Tmdb;

namespace MovieTracker.Evals;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await using HarnessHost host = new HarnessHost();

        IConfiguration configuration = host.Configuration;
        string? missingKey = ConfigChecker.FindMissingKey(configuration);
        if (missingKey is not null)
        {
            Console.WriteLine($"{missingKey} is missing from the hosted API's IConfiguration. Nothing was asked.");
            Environment.ExitCode = 1;
            return;
        }

        IReadOnlyDictionary<string, IEvalCheck> discovery = CheckDiscovery.DiscoverAll();

        string tmdbKey = configuration["TheMovieDb:Api-Key"]!;
        string omdbKey = configuration["OpenMovieDb:Api-Key"]!;

        IReadOnlyList<PlotAnchorCase> plotAnchors = PlotAnchorCaseLoader.Load();
        IReadOnlyList<CaseTemplate> templates = TemplateLoader.Load();

        using TmdbClient tmdbClient = new TmdbClient(tmdbKey);
        using OmdbClient omdbClient = new OmdbClient(omdbKey);

        HttpClient apiClient = host.CreateClient();
        AgentClient agentClient = new AgentClient(apiClient);
        EvalItemBuilder evalItemBuilder = new EvalItemBuilder(agentClient);
        CheckEvaluationRunner checkRunner = new CheckEvaluationRunner(discovery);

        EvalRunner runner = new EvalRunner(tmdbClient, omdbClient, evalItemBuilder, checkRunner, plotAnchors, templates);
        Environment.ExitCode = await runner.RunAsync();
    }
}
