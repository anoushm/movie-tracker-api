namespace MovieTracker.Evals;

public static class ReleaseYearExtractor
{
    public static int? Extract(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
        {
            return null;
        }

        string prefix = releaseDate.Substring(0, 4);
        foreach (char c in prefix)
        {
            if (!char.IsDigit(c))
            {
                return null;
            }
        }

        if (!int.TryParse(prefix, out int year))
        {
            return null;
        }

        return year;
    }
}
