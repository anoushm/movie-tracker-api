using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using MovieTracker.Evals.Agent;
using MovieTracker.Evals.Cases;
using MovieTracker.Evals.Checks;
using MovieTracker.Evals.Reporting;
using MovieTracker.Evals.Tmdb;

namespace MovieTracker.Evals;

public sealed class EvalRunner
{
    private readonly TmdbClient tmdbClient;
    private readonly OmdbClient omdbClient;
    private readonly EvalItemBuilder evalItemBuilder;
    private readonly CheckEvaluationRunner checkRunner;
    private readonly IReadOnlyList<PlotAnchorCase> plotAnchors;
    private readonly IReadOnlyList<CaseTemplate> templates;

    public EvalRunner(
        TmdbClient tmdbClient,
        OmdbClient omdbClient,
        EvalItemBuilder evalItemBuilder,
        CheckEvaluationRunner checkRunner,
        IReadOnlyList<PlotAnchorCase> plotAnchors,
        IReadOnlyList<CaseTemplate> templates)
    {
        this.tmdbClient = tmdbClient;
        this.omdbClient = omdbClient;
        this.evalItemBuilder = evalItemBuilder;
        this.checkRunner = checkRunner;
        this.plotAnchors = plotAnchors;
        this.templates = templates;
    }

    public async Task<int> RunAsync()
    {
        DateTimeOffset runAt = DateTimeOffset.UtcNow;

        RunTitleSelector runTitleSelector = new RunTitleSelector(this.tmdbClient);
        IReadOnlyList<RunTitle> runTitles;
        try
        {
            runTitles = await runTitleSelector.SelectAsync();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Precondition failure (popular title selection): {ex.Message}. Nothing was asked.");
            return 1;
        }

        Reporter.PrintPopularTitles(runTitles);

        GeneratedCaseBuilder caseBuilder = new GeneratedCaseBuilder(this.tmdbClient, this.omdbClient);
        GeneratedCaseBatch batch;
        try
        {
            batch = await caseBuilder.BuildAsync(this.templates, runTitles);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Precondition failure (generated-case assembly): {ex.Message}. Nothing was asked.");
            return 1;
        }

        int totalCount = this.plotAnchors.Count + batch.Cases.Count;
        List<CaseResult?> results = new List<CaseResult?>(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            results.Add(null);
        }

        Dictionary<string, List<(int Index, string CaseId, EvalItem Item)>> grouping =
            new Dictionary<string, List<(int, string, EvalItem)>>(StringComparer.Ordinal);

        for (int anchorIndex = 0; anchorIndex < this.plotAnchors.Count; anchorIndex++)
        {
            PlotAnchorCase anchor = this.plotAnchors[anchorIndex];
            string expectedJson = JsonSerializer.Serialize(new ListIncludesExpected(anchor.Expected));
            (CaseResult? preMark, EvalItem? item) = await this.evalItemBuilder.BuildAnchorAsync(anchor, expectedJson);
            if (preMark is not null)
            {
                results[anchorIndex] = preMark;
                continue;
            }

            if (item is not null)
            {
                if (!grouping.TryGetValue(ListIncludesCheck.CheckName, out List<(int, string, EvalItem)>? bucket))
                {
                    bucket = new List<(int, string, EvalItem)>();
                    grouping[ListIncludesCheck.CheckName] = bucket;
                }

                bucket.Add((anchorIndex, anchor.Id, item));
            }
        }

        for (int generatedIndex = 0; generatedIndex < batch.Cases.Count; generatedIndex++)
        {
            GeneratedCase generated = batch.Cases[generatedIndex];
            int slotIndex = this.plotAnchors.Count + generatedIndex;

            if (generated.NoTruthValue)
            {
                results[slotIndex] = new CaseResult(generated.Id, generated.Check, false, "no truth value") { Skipped = true };
                continue;
            }

            (CaseResult? preMark, EvalItem? item) = await this.evalItemBuilder.BuildGeneratedAsync(generated, generated.Check);
            if (preMark is not null)
            {
                results[slotIndex] = preMark;
                continue;
            }

            if (item is not null)
            {
                if (!grouping.TryGetValue(generated.Check, out List<(int, string, EvalItem)>? bucket))
                {
                    bucket = new List<(int, string, EvalItem)>();
                    grouping[generated.Check] = bucket;
                }

                bucket.Add((slotIndex, generated.Id, item));
            }
        }

        foreach (KeyValuePair<string, List<(int Index, string CaseId, EvalItem Item)>> entry in grouping)
        {
            string checkName = entry.Key;
            List<(int Index, string CaseId, EvalItem Item)> group = entry.Value;
            List<(string CaseId, EvalItem Item)> groupPairs = group
                .Select(g => (g.CaseId, g.Item))
                .ToList();
            IReadOnlyList<CaseResult> groupResults = await this.checkRunner.EvaluateGroupAsync(checkName, groupPairs);
            for (int k = 0; k < group.Count; k++)
            {
                results[group[k].Index] = groupResults[k];
            }
        }

        List<CaseResult> ordered = new List<CaseResult>(totalCount);
        foreach (CaseResult? slot in results)
        {
            if (slot is null)
            {
                throw new InvalidOperationException("EvalRunner: result slot was not populated");
            }

            ordered.Add(slot);
        }

        int passed = 0;
        int skipped = 0;
        foreach (CaseResult result in ordered)
        {
            if (result.Skipped)
            {
                skipped++;
            }
            else if (result.Passed)
            {
                passed++;
            }
        }

        int total = ordered.Count - skipped;

        List<string> checkOrder = new List<string>();
        checkOrder.Add(ListIncludesCheck.CheckName);
        foreach (CaseTemplate template in this.templates)
        {
            checkOrder.Add(template.Check);
        }

        IReadOnlyList<PerCheckCount> perCheck = BuildPerCheck(ordered, checkOrder);
        RunResult current = new RunResult(runAt, total, passed, perCheck, ordered, skipped);

        Reporter.PrintTable(ordered);
        Reporter.PrintTotals(passed, total, skipped);

        RunResult? previous = await ResultsStore.ReadPreviousAsync();
        IReadOnlyList<CheckComparison> comparison = CheckComparisonComputer.Compute(previous, current, checkOrder);
        Reporter.PrintCheckComparison(comparison);

        await ResultsStore.WriteAsync(current);
        Console.WriteLine($"Wrote {Path.Combine("MovieTracker.Evals", "results", "last-run.json")}");
        return 0;
    }

    private static IReadOnlyList<PerCheckCount> BuildPerCheck(
        IReadOnlyList<CaseResult> results,
        IReadOnlyList<string> checkOrder)
    {
        Dictionary<string, (int Passed, int Total, int Skipped)> counts =
            new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        foreach (CaseResult result in results)
        {
            if (!counts.TryGetValue(result.Check, out (int Passed, int Total, int Skipped) existing))
            {
                existing = (0, 0, 0);
            }

            if (result.Skipped)
            {
                counts[result.Check] = (existing.Passed, existing.Total, existing.Skipped + 1);
            }
            else
            {
                int passedDelta = result.Passed ? 1 : 0;
                counts[result.Check] = (existing.Passed + passedDelta, existing.Total + 1, existing.Skipped);
            }
        }

        List<PerCheckCount> output = new List<PerCheckCount>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string check in checkOrder)
        {
            if (!seen.Add(check))
            {
                continue;
            }

            if (counts.TryGetValue(check, out (int Passed, int Total, int Skipped) found))
            {
                output.Add(new PerCheckCount(check, found.Passed, found.Total, found.Skipped));
            }
        }

        foreach (KeyValuePair<string, (int Passed, int Total, int Skipped)> pair in counts)
        {
            if (seen.Contains(pair.Key))
            {
                continue;
            }

            output.Add(new PerCheckCount(pair.Key, pair.Value.Passed, pair.Value.Total, pair.Value.Skipped));
        }

        return output;
    }
}
