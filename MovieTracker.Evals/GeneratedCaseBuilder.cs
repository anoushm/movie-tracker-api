using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public sealed class GeneratedCaseBuilder
{
    private readonly TmdbClient tmdbClient;
    private readonly OmdbClient omdbClient;

    private readonly Dictionary<int, MovieDetails> detailsCache = new Dictionary<int, MovieDetails>();
    private readonly Dictionary<int, MovieCredits> creditsCache = new Dictionary<int, MovieCredits>();
    private readonly Dictionary<int, ActorFilmography> filmographyCache = new Dictionary<int, ActorFilmography>();
    private readonly Dictionary<int, IReadOnlyList<string>> trailerKeysCache = new Dictionary<int, IReadOnlyList<string>>();

    public GeneratedCaseBuilder(TmdbClient tmdbClient, OmdbClient omdbClient)
    {
        this.tmdbClient = tmdbClient;
        this.omdbClient = omdbClient;
    }

    public async Task<GeneratedCaseBatch> BuildAsync(
        IReadOnlyList<CaseTemplate> templates,
        IReadOnlyList<RunTitle> runTitles)
    {
        List<GeneratedCase> cases = new List<GeneratedCase>();
        int trailerProducedCount = 0;

        foreach (CaseTemplate template in templates)
        {
            RunTitle selected = runTitles[Random.Shared.Next(runTitles.Count)];
            string generatedId = $"{template.Id}-{selected.Slot}";

            switch (template.Check)
            {
                case TemplateCheck.Director:
                {
                    GeneratedCase produced = await this.BuildDirectorAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.FollowUpDirector:
                {
                    GeneratedCase produced = await this.BuildFollowUpDirectorAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.ActorFilmography:
                {
                    GeneratedCase produced = await this.BuildActorFilmographyAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.IdentifyByPlot:
                {
                    GeneratedCase produced = await this.BuildIdentifyByPlotAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.GenreDecade:
                {
                    GeneratedCase produced = await this.BuildGenreDecadeAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.Rating:
                {
                    GeneratedCase produced = await this.BuildRatingAsync(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                case TemplateCheck.Trailer:
                {
                    GeneratedCase? produced = await this.TryBuildTrailerAsync(template, selected, generatedId);
                    if (produced is not null)
                    {
                        cases.Add(produced);
                        trailerProducedCount++;
                    }

                    break;
                }
                case TemplateCheck.FunnyFactNotEmpty:
                {
                    GeneratedCase produced = this.BuildFunnyFactNotEmpty(template, selected, generatedId);
                    cases.Add(produced);
                    break;
                }
                default:
                {
                    throw new InvalidOperationException($"Unhandled TemplateCheck: {template.Check}");
                }
            }
        }

        return new GeneratedCaseBatch(cases, trailerProducedCount);
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
        IReadOnlyList<string> rawTurns = template.Turns ?? Array.Empty<string>();
        List<string> resolvedTurns = new List<string>(rawTurns.Count);
        foreach (string turn in rawTurns)
        {
            resolvedTurns.Add(turn.Replace("{title}", selected.Title));
        }

        string firstTurn = resolvedTurns.Count > 0 ? resolvedTurns[0] : string.Empty;

        if (string.IsNullOrWhiteSpace(credits.DirectorName))
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
        string? overview = details.Overview;
        string strippedOverview = this.StripTitleWords(overview ?? string.Empty, selected.Title);
        string question = this.RequireTemplate(template).Replace("{overview}", strippedOverview);

        if (string.IsNullOrWhiteSpace(overview))
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
        string question = this.RequireTemplate(template).Replace("{title}", selected.Title);

        if (string.IsNullOrWhiteSpace(details.ImdbId))
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

    private async Task<GeneratedCase?> TryBuildTrailerAsync(CaseTemplate template, RunTitle selected, string generatedId)
    {
        IReadOnlyList<string> keys = await this.GetTrailerKeysCachedAsync(selected.TmdbId);
        if (keys.Count == 0)
        {
            return null;
        }

        string question = this.RequireTemplate(template).Replace("{title}", selected.Title);
        string expectedJson = JsonSerializer.Serialize(new TrailerExpected(keys));
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, expectedJson, NoTruthValue: false);
    }

    private GeneratedCase BuildFunnyFactNotEmpty(CaseTemplate template, RunTitle selected, string generatedId)
    {
        string question = this.RequireTemplate(template).Replace("{title}", selected.Title);
        return new GeneratedCase(generatedId, question, Turns: null, template.Check, ExpectedJson: null, NoTruthValue: false);
    }

    private GeneratedCase NoTruth(string id, string question, IReadOnlyList<string>? Turns, TemplateCheck check)
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
