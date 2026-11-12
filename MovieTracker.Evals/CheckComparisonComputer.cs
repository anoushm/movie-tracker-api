using System.Collections.Generic;

namespace MovieTracker.Evals;

public static class CheckComparisonComputer
{
    public static IReadOnlyList<CheckComparison> Compute(RunResult? previous, RunResult current)
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

        List<CheckComparison> rows = new List<CheckComparison>();
        foreach (string check in CheckOrder.All)
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
            if (currentByCheck.TryGetValue(check, out PerCheckCount? currentEntry))
            {
                currentPassed = currentEntry.Passed;
                currentTotal = currentEntry.Total;
            }

            rows.Add(new CheckComparison(check, previousPassed, previousTotal, currentPassed, currentTotal));
        }

        int? previousTotalPassed = previous?.Passed;
        int? previousTotalTotal = previous?.Total;
        rows.Add(new CheckComparison("total", previousTotalPassed, previousTotalTotal, current.Passed, current.Total));

        return rows;
    }
}
