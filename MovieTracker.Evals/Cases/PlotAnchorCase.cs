using System.Collections.Generic;

namespace MovieTracker.Evals.Cases;

public sealed class PlotAnchorCase
{
    public string Id { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public List<string> Expected { get; set; } = new List<string>();
}
