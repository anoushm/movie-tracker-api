using System;
using System.Collections.Generic;
using System.Text;

namespace MovieTracker.Evals;

public static class Reporter
{
    public static void PrintPopularTitles(IReadOnlyList<RunTitle> runTitles)
    {
        StringBuilder builder = new StringBuilder("Popular titles for this run:");
        bool first = true;
        foreach (RunTitle runTitle in runTitles)
        {
            if (first)
            {
                builder.Append(' ');
                first = false;
            }
            else
            {
                builder.Append(", ");
            }

            builder.Append(runTitle.Region);
            builder.Append(" slot ");
            builder.Append(runTitle.Slot);
            builder.Append(" = ");
            builder.Append(runTitle.Title);
        }

        Console.WriteLine(builder.ToString());
    }

    public static void PrintTrailerProducedCount(int count)
    {
        Console.WriteLine($"trailer produced {count} case(s)");
    }

    public static void PrintTable(IReadOnlyList<CaseResult> results)
    {
        int idWidth = HeaderWidth("id", results, result => result.Id);
        int checkWidth = HeaderWidth("check", results, result => result.Check);
        const int resultWidth = 6;

        string header = $"{PadRight("id", idWidth)}  {PadRight("check", checkWidth)}  {PadRight("result", resultWidth)}  reason";
        Console.WriteLine();
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));

        foreach (CaseResult result in results)
        {
            string resultText = result.Passed ? "PASS" : "FAIL";
            Console.WriteLine($"{PadRight(result.Id, idWidth)}  {PadRight(result.Check, checkWidth)}  {PadRight(resultText, resultWidth)}  {result.Reason}");
        }
    }

    public static void PrintTotals(int passed, int total)
    {
        Console.WriteLine();
        Console.WriteLine($"Total: {passed} / {total}");
    }

    public static void PrintCheckComparison(IReadOnlyList<CheckComparison> comparison)
    {
        bool anyPreviousSide = false;
        foreach (CheckComparison row in comparison)
        {
            if (row.PreviousPassed is not null || row.PreviousTotal is not null)
            {
                anyPreviousSide = true;
                break;
            }
        }

        if (!anyPreviousSide)
        {
            Console.WriteLine("(No previous run on disk; skipping per-check comparison.)");
            return;
        }

        int labelWidth = 0;
        foreach (CheckComparison row in comparison)
        {
            if (row.Check.Length > labelWidth)
            {
                labelWidth = row.Check.Length;
            }
        }

        Console.WriteLine("Per-check comparison (previous -> current):");
        foreach (CheckComparison row in comparison)
        {
            string label = PadRight(row.Check + ":", labelWidth + 1);
            string previous = FormatSide(row.PreviousPassed, row.PreviousTotal);
            string current = FormatSide(row.CurrentPassed, row.CurrentTotal);
            Console.WriteLine($"  {label} {previous} -> {current}");
        }
    }

    private static string FormatSide(int? passed, int? total)
    {
        if (passed is null || total is null)
        {
            return "-";
        }

        return $"{passed}/{total}";
    }

    private static int HeaderWidth(string header, IReadOnlyList<CaseResult> results, Func<CaseResult, string> selector)
    {
        int width = header.Length;
        foreach (CaseResult result in results)
        {
            string value = selector(result);
            if (value.Length > width)
            {
                width = value.Length;
            }
        }

        return width;
    }

    private static string PadRight(string value, int width)
    {
        if (value.Length >= width)
        {
            return value;
        }

        return value + new string(' ', width - value.Length);
    }
}
