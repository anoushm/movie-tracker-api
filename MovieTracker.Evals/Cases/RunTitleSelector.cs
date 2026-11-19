using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTracker.Evals.Tmdb;

namespace MovieTracker.Evals.Cases;

public sealed class RunTitleSelector
{
    private readonly TmdbClient tmdbClient;

    public RunTitleSelector(TmdbClient tmdbClient)
    {
        this.tmdbClient = tmdbClient;
    }

    public async Task<IReadOnlyList<RunTitle>> SelectAsync()
    {
        IReadOnlyList<MovieSummary> results = await this.tmdbClient.GetPopularAsync();
        if (results.Count < 2)
        {
            throw new InvalidOperationException("TMDb /movie/popular?page=1 returned fewer than 2 entries; need at least 2");
        }

        List<int> availableIndices = new List<int>(results.Count);
        for (int index = 0; index < results.Count; index++)
        {
            availableIndices.Add(index);
        }

        int firstIndex = availableIndices[Random.Shared.Next(availableIndices.Count)];
        availableIndices.Remove(firstIndex);
        int secondIndex = availableIndices[Random.Shared.Next(availableIndices.Count)];

        MovieSummary firstPick = results[firstIndex];
        MovieSummary secondPick = results[secondIndex];

        List<RunTitle> picked = new List<RunTitle>
        {
            new RunTitle(firstPick.Id, firstPick.Title, 1),
            new RunTitle(secondPick.Id, secondPick.Title, 2),
        };

        return picked;
    }
}
