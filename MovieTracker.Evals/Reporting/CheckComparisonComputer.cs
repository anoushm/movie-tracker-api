using System.Collections.Generic;

namespace MovieTracker.Evals.Reporting;

public static class CheckComparisonComputer
{
    public static IReadOnlyList<CheckComparison> Compute(
        RunResult? previous,
        RunResult current,
        IReadOnlyList<string> checkOrder)
    {
        Dictionary<string, PerCheckCount>? previousByCheck = null;
        if (previous is not null)
        {
            previousByCheck = new Dictionary<string, PerCheckCount>();
            foreach (PerCheckCount entry in previous.PerCheck)
            {
                previousByCheck[entry.Check] = entry;
            }
        }

        Dictionary<string, PerCheckCount> currentByCheck = new Dictionary<string, PerCheckCount>();
        foreach (PerCheckCount entry in current.PerCheck)
        {
            currentByCheck[entry.Check] = entry;
        }

        List<string> orderedChecks = new List<string>();
        HashSet<string> seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string check in checkOrder)
        {
            if (seen.Add(check))
            {
                orderedChecks.Add(check);
            }
        }

        if (previousByCheck is not null)
        {
            foreach (PerCheckCount entry in previous!.PerCheck)
            {
                if (seen.Add(entry.Check))
                {
                    orderedChecks.Add(entry.Check);
                }
            }
        }

        List<CheckComparison> rows = new List<CheckComparison>();
        foreach (string check in orderedChecks)
        {
            int? previousPassed = null;
            int? previousTotal = null;
            if (previousByCheck is not null && previousByCheck.TryGetValue(check, out PerCheckCount? previousEntry))
            {
                previousPassed = previousEntry.Passed;
                previousTotal = previousEntry.Total;
            }

            int? currentPassed = null;
            int? currentTotal = null;
            int? currentSkipped = null;
            if (currentByCheck.TryGetValue(check, out PerCheckCount? currentEntry))
            {
                currentPassed = currentEntry.Passed;
                currentTotal = currentEntry.Total;
                currentSkipped = currentEntry.Skipped;
            }

            rows.Add(new CheckComparison(check, previousPassed, previousTotal, currentPassed, currentTotal, currentSkipped));
        }

        int? previousTotalPassed = previous?.Passed;
        int? previousTotalTotal = previous?.Total;
        rows.Add(new CheckComparison("total", previousTotalPassed, previousTotalTotal, current.Passed, current.Total, current.Skipped));

        return rows;
    }
}
