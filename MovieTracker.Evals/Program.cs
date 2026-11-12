using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;

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

        RunTitleSelector runTitleSelector = new RunTitleSelector(tmdbClient);
        IReadOnlyList<RunTitle> runTitles;
        try
        {
            runTitles = await runTitleSelector.SelectAsync();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Precondition failure (popular title selection): {ex.Message}. Nothing was asked.");
            Environment.ExitCode = 1;
            return;
        }

        Reporter.PrintPopularTitles(runTitles);

        GeneratedCaseBuilder caseBuilder = new GeneratedCaseBuilder(tmdbClient, omdbClient);
        GeneratedCaseBatch batch;
        try
        {
            batch = await caseBuilder.BuildAsync(templates, runTitles);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Precondition failure (generated-case assembly): {ex.Message}. Nothing was asked.");
            Environment.ExitCode = 1;
            return;
        }

        Reporter.PrintTrailerProducedCount(batch.TrailerProducedCount);

        DateTimeOffset runAt = DateTimeOffset.UtcNow;
        HttpClient apiClient = host.CreateClient();
        AgentClient agentClient = new AgentClient(apiClient);
        EvalItemBuilder evalItemBuilder = new EvalItemBuilder(agentClient);
        CheckEvaluationRunner checkRunner = new CheckEvaluationRunner(discovery);

        List<CaseResult> preMarked = new List<CaseResult>();
        List<(string CaseId, string CheckName, EvalItem Item, int OriginalOrder)> toGrade = new List<(string, string, EvalItem, int)>();

        int order = 0;

        foreach (PlotAnchorCase anchor in plotAnchors)
        {
            string expectedJson = JsonSerializer.Serialize(new ListIncludesExpected(anchor.Expected));
            (CaseResult? preMark, EvalItem? item) = await evalItemBuilder.BuildAnchorAsync(anchor, expectedJson);
            if (preMark is not null)
            {
                preMarked.Add(preMark);
            }
            else if (item is not null)
            {
                toGrade.Add((anchor.Id, "listIncludes", item, order));
            }

            order++;
        }

        foreach (GeneratedCase generated in batch.Cases)
        {
            string checkName = TemplateCheckToString(generated.Check);

            if (generated.NoTruthValue)
            {
                preMarked.Add(new CaseResult(generated.Id, checkName, false, "no truth value"));
                order++;
                continue;
            }

            (CaseResult? preMark, EvalItem? item) = await evalItemBuilder.BuildGeneratedAsync(generated, checkName);
            if (preMark is not null)
            {
                preMarked.Add(preMark);
            }
            else if (item is not null)
            {
                toGrade.Add((generated.Id, checkName, item, order));
            }

            order++;
        }

        List<(string CaseId, string CheckName, CaseResult Result, int OriginalOrder)> graded = new List<(string, string, CaseResult, int)>();

        IEnumerable<IGrouping<string, (string CaseId, string CheckName, EvalItem Item, int OriginalOrder)>> grouped =
            toGrade.GroupBy(pair => pair.CheckName);

        foreach (IGrouping<string, (string CaseId, string CheckName, EvalItem Item, int OriginalOrder)> group in grouped)
        {
            List<(string CaseId, EvalItem Item)> groupPairs = group
                .Select(pair => (pair.CaseId, pair.Item))
                .ToList();

            IReadOnlyList<CaseResult> groupResults = await checkRunner.EvaluateGroupAsync(group.Key, groupPairs);

            int inGroupIndex = 0;
            foreach ((string caseId, string checkName, EvalItem _, int originalOrder) in group)
            {
                CaseResult groupResult = groupResults[inGroupIndex];
                graded.Add((caseId, checkName, groupResult, originalOrder));
                inGroupIndex++;
            }
        }

        Dictionary<string, (CaseResult Result, int OriginalOrder)> byId = new Dictionary<string, (CaseResult, int)>(StringComparer.Ordinal);
        foreach (CaseResult preMark in preMarked)
        {
            int preOrder = FindOriginalOrder(plotAnchors, batch.Cases, preMark.Id);
            byId[preMark.Id] = (preMark, preOrder);
        }

        foreach ((string caseId, string _, CaseResult result, int originalOrder) in graded)
        {
            byId[caseId] = (result, originalOrder);
        }

        List<CaseResult> ordered = byId.Values
            .OrderBy(pair => pair.OriginalOrder)
            .Select(pair => pair.Result)
            .ToList();

        int passed = 0;
        foreach (CaseResult result in ordered)
        {
            if (result.Passed)
            {
                passed++;
            }
        }

        IReadOnlyList<PerCheckCount> perCheck = BuildPerCheck(ordered);
        RunResult current = new RunResult(runAt, ordered.Count, passed, perCheck, ordered);

        Reporter.PrintTable(ordered);
        Reporter.PrintTotals(passed, ordered.Count);

        RunResult? previous = await ResultsStore.ReadPreviousAsync();
        IReadOnlyList<CheckComparison> comparison = CheckComparisonComputer.Compute(previous, current);
        Reporter.PrintCheckComparison(comparison);

        await ResultsStore.WriteAsync(current);
        Console.WriteLine($"Wrote {System.IO.Path.Combine("MovieTracker.Evals", "results", "last-run.json")}");
    }

    private static int FindOriginalOrder(
        IReadOnlyList<PlotAnchorCase> anchors,
        IReadOnlyList<GeneratedCase> generated,
        string caseId)
    {
        int cursor = 0;
        foreach (PlotAnchorCase anchor in anchors)
        {
            if (anchor.Id == caseId)
            {
                return cursor;
            }

            cursor++;
        }

        foreach (GeneratedCase g in generated)
        {
            if (g.Id == caseId)
            {
                return cursor;
            }

            cursor++;
        }

        return cursor;
    }

    private static IReadOnlyList<PerCheckCount> BuildPerCheck(IReadOnlyList<CaseResult> results)
    {
        Dictionary<string, (int Passed, int Total)> counts = new Dictionary<string, (int, int)>();
        foreach (CaseResult result in results)
        {
            if (!counts.TryGetValue(result.Check, out (int Passed, int Total) existing))
            {
                existing = (0, 0);
            }

            int passedDelta = result.Passed ? 1 : 0;
            counts[result.Check] = (existing.Passed + passedDelta, existing.Total + 1);
        }

        List<PerCheckCount> ordered = new List<PerCheckCount>();
        foreach (string check in CheckOrder.All)
        {
            if (counts.TryGetValue(check, out (int Passed, int Total) found))
            {
                ordered.Add(new PerCheckCount(check, found.Passed, found.Total));
            }
        }

        foreach (KeyValuePair<string, (int Passed, int Total)> pair in counts)
        {
            if (!CheckOrder.All.Contains(pair.Key))
            {
                ordered.Add(new PerCheckCount(pair.Key, pair.Value.Passed, pair.Value.Total));
            }
        }

        return ordered;
    }

    private static string TemplateCheckToString(TemplateCheck check)
    {
        return check switch
        {
            TemplateCheck.Director => "director",
            TemplateCheck.ActorFilmography => "actorFilmography",
            TemplateCheck.IdentifyByPlot => "identifyByPlot",
            TemplateCheck.GenreDecade => "genreDecade",
            TemplateCheck.Rating => "rating",
            TemplateCheck.Trailer => "trailer",
            TemplateCheck.FollowUpDirector => "followUpDirector",
            TemplateCheck.FunnyFactNotEmpty => "funnyFactNotEmpty",
            _ => throw new InvalidOperationException($"Unknown TemplateCheck: {check}"),
        };
    }
}
