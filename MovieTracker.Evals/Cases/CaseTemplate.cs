using System.Collections.Generic;

namespace MovieTracker.Evals.Cases;

public sealed class CaseTemplate
{
    public string Id { get; set; } = string.Empty;

    public string? Template { get; set; }

    public IReadOnlyList<string>? Turns { get; set; }

    public string Check { get; set; } = string.Empty;
}
