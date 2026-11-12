using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed class CaseTemplate
{
    public string Id { get; set; } = string.Empty;

    public string? Template { get; set; }

    public IReadOnlyList<string>? Turns { get; set; }

    public TemplateCheck Check { get; set; }
}
