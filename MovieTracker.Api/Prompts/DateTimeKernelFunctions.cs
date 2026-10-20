using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace MovieTracker.Api.Prompts;

public class DateTimeKernelFunctions
{
    public static IEnumerable<AITool> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(Today),
            AIFunctionFactory.Create(ThisMonth),
            AIFunctionFactory.Create(ThisYear),
            AIFunctionFactory.Create(PastYearsRange),
            AIFunctionFactory.Create(PastMonthsRange),
            AIFunctionFactory.Create(PastDaysRange),
            AIFunctionFactory.Create(OffsetDate),
        ];
    }

    [Description("Today in ISO format (YYYY-MM-DD)")]
    public static string Today()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd");
    }

    [Description("This month (first day) in ISO format (YYYY-MM)")]
    public static string ThisMonth()
    {
        return DateTime.UtcNow.ToString("yyyy-MM");
    }

    [Description("This year in ISO format (YYYY)")]
    public static string ThisYear()
    {
        return DateTime.UtcNow.Year.ToString();
    }

    [Description("ISO-8601 interval for the past <years> full years up to today. " +
                 "Example: PastYearsRange(3) → 2022-06-13/2025-06-13")]
    public static string PastYearsRange(
        [Description("Number of years to look back (e.g. 3 = last three years)")] int years)
    {
        DateTime end = DateTime.UtcNow.Date;
        DateTime start = end.AddYears(-years);
        return $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}";
    }

    [Description("ISO-8601 interval for the past <months> full months up to today. " +
                 "Example: PastMonthsRange(6) → 2024-12-13/2025-06-13")]
    public static string PastMonthsRange(int months)
    {
        DateTime end = DateTime.UtcNow.Date;
        DateTime start = end.AddMonths(-months);
        return $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}";
    }

    [Description("ISO-8601 interval for the past <days> days up to today.")]
    public static string PastDaysRange(int days)
    {
        DateTime end = DateTime.UtcNow.Date;
        DateTime start = end.AddDays(-days);
        return $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}";
    }

    [Description("Offset any ISO date (YYYY-MM-DD) by N units. Units = d, m, y. " +
                 "Example: OffsetDate(\"2022-05-20\", 10, \"d\")")]
    public static string OffsetDate(
        string isoDate,
        int amount,
        [Description("Unit (d=days, m=months, y=years)")] string unit)
    {
        DateTime dt = DateTime.Parse(isoDate, CultureInfo.InvariantCulture);
        DateTime shifted = unit switch
        {
            "d" => dt.AddDays(amount),
            "m" => dt.AddMonths(amount),
            "y" => dt.AddYears(amount),
            _ => throw new ArgumentException("unit must be d, m, or y")
        };
        return shifted.ToString("yyyy-MM-dd");
    }
}
