# Contract: `eval-movie-cases.json`

Shape of the file lives at `MovieTracker.Evals/eval-movie-cases.json` and is loaded by `PlotAnchorCaseLoader.Load`. These are the plot-anchor cases, always evaluated under `ListIncludesCheck.CheckName`.

**Structural change in this slice**: remove the `check` field from each entry (FR-015).

## New shape

```json
{
  "cases": [
    {
      "id": "<string>",
      "question": "<string>",
      "expected": ["<string title>", "..."]
    }
  ]
}
```

- `id`: anchor id (unchanged).
- `question`: user question to the agent (unchanged).
- `expected`: list of titles the agent's answer must include (unchanged).
- No `check` field. The harness hard-codes `ListIncludesCheck.CheckName` for every anchor in this file.

## Current file (after edit)

```json
{
  "cases": [
    {
      "id": "plot-ship-sinks",
      "question": "Show me movies where a ship sinks at the end",
      "expected": ["Titanic"]
    },
    {
      "id": "plot-mob-family",
      "question": "Show me classic mobster movies about a crime family",
      "expected": ["The Godfather"]
    }
  ]
}
```

## Reader behavior

- `PlotAnchorCaseLoader.Load()` parses entries into `PlotAnchorCase` instances (the `PlotAnchorCase` type stays a `sealed class` with public setters for `Id`, `Question`, `Expected`; the old `Check` property is removed).
- Downstream, `EvalRunner` passes `ListIncludesCheck.CheckName` as the check string when grading each anchor.
