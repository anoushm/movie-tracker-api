using System.Collections.Generic;

namespace MovieTracker.Evals;

public sealed class PlotAnchorCase
{
    public string Id { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public CheckKind Check { get; set; }

    public List<string> Expected { get; set; } = new List<string>();
}
