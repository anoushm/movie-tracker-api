using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MovieTracker.Evals.Checks;
using MovieTracker.Evals.Tmdb;

namespace MovieTracker.Evals.Cases;

public sealed class GeneratedCaseBuilder
{
    private readonly TmdbClient tmdbClient;
    private readonly OmdbClient omdbClient;
    private readonly RunTitleSelector ratingTitleSelector;

    private readonly Dictionary<int, MovieDetails> detailsCache = new Dictionary<int, MovieDetails>();
    private readonly Dictionary<int, MovieCredits> creditsCache = new Dictionary<int, MovieCredits>();
    private readonly Dictionary<int, ActorFilmography> filmographyCache = new Dictionary<int, ActorFilmography>();
    private readonly Dictionary<int, IReadOnlyList<string>> trailerKeysCache = new Dictionary<int, IReadOnlyList<string>>();

    public GeneratedCaseBuilder(TmdbClient tmdbClient, OmdbClient omdbClient)
    {
        this.tmdbClient = tmdbClient;
        this.omdbClient = omdbClient;
        this.ratingTitleSelector = new RunTitleSelector(tmdbClient);
    }

    public async Task<GeneratedCaseBatch> BuildAsync(
        IReadOnlyList<CaseTemplate> templates,
        IReadOnlyList<RunTitle> runTitles)
    {
        List<GeneratedCase> cases = new List<GeneratedCase>();
        IReadOnlyList<RunTitle>? ratingTitles = null;

        foreach (CaseTemplate template in templates)
        {
            foreach (RunTitle selected in runTitles)
            {
                string generatedId = $"{template.Id}-{selected.Slot}";

                switch (template.Check)
                {
                    case DirectorCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildDirectorAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case FollowUpDirectorCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildFollowUpDirectorAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case ActorFilmographyCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildActorFilmographyAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case IdentifyByPlotCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildIdentifyByPlotAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case GenreDecadeCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildGenreDecadeAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case RatingCheck.CheckName:
                    {
                        if (ratingTitles is null)
                        {
                            ratingTitles = await this.ratingTitleSelector.SelectRatingTitlesAsync();
                        }
                        RunTitle ratingTitle = ratingTitles[selected.Slot - 1];
                        GeneratedCase produced = await this.BuildRatingAsync(template, ratingTitle, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case TrailerCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildTrailerAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    case FunnyFactNotEmptyCheck.CheckName:
                    {
                        GeneratedCase produced = await this.BuildFunnyFactNotEmptyAsync(template, selected, generatedId);
                        cases.Add(produced);
                        break;
                    }
                    default:
                    {
                        throw new InvalidOperationException($"Unhandled check name: {template.Check}");
                    }
                }
            }
        }

        return new GeneratedCaseBatch(cases);
    }

    private async Task<GeneratedCase> BuildDirectorAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieCredits credits = await this.GetCreditsCachedAsync(selected.TmdbId);
        string question = this.RequireTemplate(template).Replace("{title}", selected.Title);

        if (string.IsNullOrWhiteSpace(credits.DirectorName))
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new DirectorExpected(credits.DirectorName));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildFollowUpDirectorAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieCredits credits = await this.GetCreditsCachedAsync(selected.TmdbId);
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        int? year = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string yearText = year?.ToString() ?? string.Empty;

        IReadOnlyList<string> rawTurns = template.Turns ?? Array.Empty<string>();
        List<string> resolvedTurns = new List<string>(rawTurns.Count);
        foreach (string turn in rawTurns)
        {
            resolvedTurns.Add(turn.Replace("{title}", selected.Title).Replace("{year}", yearText));
        }

        string firstTurn = resolvedTurns.Count > 0 ? resolvedTurns[0] : string.Empty;

        if (string.IsNullOrWhiteSpace(credits.DirectorName) || year is null)
        {
            return this.NoTruth(generatedId, firstTurn, resolvedTurns, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new DirectorExpected(credits.DirectorName));
        return new GeneratedCase(generatedId, firstTurn, resolvedTurns, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildActorFilmographyAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieCredits credits = await this.GetCreditsCachedAsync(selected.TmdbId);
        string actorName = credits.LeadActorName ?? string.Empty;
        string question = this.RequireTemplate(template).Replace("{actor}", actorName);

        if (credits.LeadActorId is null || string.IsNullOrWhiteSpace(credits.LeadActorName))
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        ActorFilmography filmography = await this.GetFilmographyCachedAsync(credits.LeadActorId.Value);
        if (filmography.MovieIds.Count == 0 && filmography.MovieTitles.Count == 0)
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new ActorFilmographyExpected(
            credits.LeadActorName,
            filmography.MovieIds,
            filmography.MovieTitles));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildIdentifyByPlotAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        int? year = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string yearText = year?.ToString() ?? string.Empty;
        string? overview = details.Overview;
        string strippedOverview = this.StripTitleWords(overview ?? string.Empty, selected.Title);
        string question = this.RequireTemplate(template)
            .Replace("{year}", yearText)
            .Replace("{overview}", strippedOverview);

        if (string.IsNullOrWhiteSpace(overview) || year is null)
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new IdentifyByPlotExpected(selected.TmdbId, selected.Title));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildGenreDecadeAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        string genreName = details.Genres.Count > 0 ? details.Genres[0].Name : string.Empty;
        int? yearValue = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string decadeText = string.Empty;
        int decadeStart = 0;
        if (yearValue is not null)
        {
            decadeStart = yearValue.Value - (yearValue.Value % 10);
            decadeText = decadeStart.ToString();
        }

        string question = this.RequireTemplate(template)
            .Replace("{genre}", genreName)
            .Replace("{decade}", decadeText);

        if (details.Genres.Count == 0 || string.IsNullOrEmpty(decadeText))
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        MovieGenre firstGenre = details.Genres[0];
        string expectedJson = JsonSerializer.Serialize(new GenreDecadeExpected(firstGenre.Id, firstGenre.Name, decadeStart));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildRatingAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        int? year = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string yearText = year?.ToString() ?? string.Empty;
        string question = this.RequireTemplate(template)
            .Replace("{title}", selected.Title)
            .Replace("{year}", yearText);

        if (string.IsNullOrWhiteSpace(details.ImdbId) || year is null)
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string? rating = await this.omdbClient.GetImdbRatingAsync(details.ImdbId);
        if (string.IsNullOrWhiteSpace(rating))
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new RatingExpected(rating));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildTrailerAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        IReadOnlyList<string> keys = await this.GetTrailerKeysCachedAsync(selected.TmdbId);
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        int? year = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string yearText = year?.ToString() ?? string.Empty;
        string question = this.RequireTemplate(template)
            .Replace("{title}", selected.Title)
            .Replace("{year}", yearText);

        if (keys.Count == 0 || year is null)
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        string expectedJson = JsonSerializer.Serialize(new TrailerExpected(keys));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private async Task<GeneratedCase> BuildFunnyFactNotEmptyAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        MovieDetails details = await this.GetDetailsCachedAsync(selected.TmdbId);
        int? year = ReleaseYearExtractor.Extract(details.ReleaseDate);
        string yearText = year?.ToString() ?? string.Empty;
        string question = this.RequireTemplate(template)
            .Replace("{title}", selected.Title)
            .Replace("{year}", yearText);

        if (year is null)
        {
            return this.NoTruth(generatedId, question, Turns: null, template.Check);
        }

        return new GeneratedCase(generatedId, question, Turns: null, template.Check, ExpectedJson: null, NoTruthValue: false);
    }

    private GeneratedCase NoTruth(string id, string question, IReadOnlyList<string>? Turns, string check)
    {
        return new GeneratedCase(id, question, Turns, check, ExpectedJson: null, NoTruthValue: true);
    }

    private string RequireTemplate(CaseTemplate template)
    {
        if (string.IsNullOrEmpty(template.Template))
        {
            throw new InvalidOperationException($"Template '{template.Id}' is missing a single-turn 'template' string");
        }

        return template.Template;
    }

    private string StripTitleWords(string overview, string title)
    {
        if (string.IsNullOrEmpty(overview) || string.IsNullOrEmpty(title))
        {
            return overview;
        }

        string[] titleWords = title.Split(new char[] { ' ', '\t', '-', ':', '!', '?', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
        string result = overview;
        foreach (string word in titleWords.OrderByDescending(w => w.Length))
        {
            if (word.Length == 0)
            {
                continue;
            }

            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                "[redacted]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return result;
    }

    private async Task<MovieDetails> GetDetailsCachedAsync(int tmdbId)
    {
        if (this.detailsCache.TryGetValue(tmdbId, out MovieDetails? cached))
        {
            return cached;
        }

        MovieDetails fresh = await this.tmdbClient.GetMovieDetailsAsync(tmdbId);
        this.detailsCache[tmdbId] = fresh;
        return fresh;
    }

    private async Task<MovieCredits> GetCreditsCachedAsync(int tmdbId)
    {
        if (this.creditsCache.TryGetValue(tmdbId, out MovieCredits? cached))
        {
            return cached;
        }

        MovieCredits fresh = await this.tmdbClient.GetCreditsAsync(tmdbId);
        this.creditsCache[tmdbId] = fresh;
        return fresh;
    }

    private async Task<ActorFilmography> GetFilmographyCachedAsync(int personId)
    {
        if (this.filmographyCache.TryGetValue(personId, out ActorFilmography? cached))
        {
            return cached;
        }

        ActorFilmography fresh = await this.tmdbClient.GetPersonMovieCreditsAsync(personId);
        this.filmographyCache[personId] = fresh;
        return fresh;
    }

    private async Task<IReadOnlyList<string>> GetTrailerKeysCachedAsync(int tmdbId)
    {
        if (this.trailerKeysCache.TryGetValue(tmdbId, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        IReadOnlyList<string> fresh = await this.tmdbClient.GetTrailerVideoKeysAsync(tmdbId);
        this.trailerKeysCache[tmdbId] = fresh;
        return fresh;
    }
}
