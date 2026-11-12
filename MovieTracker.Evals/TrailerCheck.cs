using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class TrailerCheck : IEvalCheck
{
    private readonly Regex trailerTagPattern = new Regex(
        @"\[TRAILER\]([^\[\]]+)\[/TRAILER\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string[] youtubeHosts = new string[]
    {
        "www.youtube.com",
        "youtube.com",
        "m.youtube.com",
        "youtu.be",
    };

    public string Name => "trailer";

    public EvalCheckResult Evaluate(EvalItem item)
    {
        if (string.IsNullOrEmpty(item.ExpectedOutput))
        {
            return new EvalCheckResult(false, "no expected video keys", this.Name);
        }

        TrailerExpected? expected;
        try
        {
            expected = JsonSerializer.Deserialize<TrailerExpected>(item.ExpectedOutput);
        }
        catch (JsonException ex)
        {
            return new EvalCheckResult(false, $"could not parse ExpectedOutput: {ex.Message}", this.Name);
        }

        if (expected is null || expected.VideoKeys is null || expected.VideoKeys.Count == 0)
        {
            return new EvalCheckResult(false, "no expected video keys", this.Name);
        }

        HashSet<string> validKeys = new HashSet<string>(expected.VideoKeys);
        string answer = item.Response ?? string.Empty;

        MatchCollection tagMatches = this.trailerTagPattern.Matches(answer);
        if (tagMatches.Count == 0)
        {
            return new EvalCheckResult(false, "no [TRAILER] tag found", this.Name);
        }

        foreach (Match tagMatch in tagMatches)
        {
            string url = tagMatch.Groups[1].Value.Trim();
            string? key = this.TryExtractYouTubeKey(url);
            if (key is null)
            {
                continue;
            }

            if (validKeys.Contains(key))
            {
                return new EvalCheckResult(true, "ok", this.Name);
            }
        }

        return new EvalCheckResult(false, "no [TRAILER] tag carried a known YouTube video key", this.Name);
    }

    private string? TryExtractYouTubeKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string host = uri.Host;
        bool hostIsYouTube = false;
        foreach (string allowed in this.youtubeHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                hostIsYouTube = true;
                break;
            }
        }

        if (!hostIsYouTube)
        {
            return null;
        }

        if (string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            string trimmedPath = uri.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(trimmedPath))
            {
                return null;
            }

            int slash = trimmedPath.IndexOf('/');
            return slash < 0 ? trimmedPath : trimmedPath.Substring(0, slash);
        }

        NameValueCollection query = HttpUtility.ParseQueryString(uri.Query);
        string? v = query["v"];
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        return v;
    }
}
