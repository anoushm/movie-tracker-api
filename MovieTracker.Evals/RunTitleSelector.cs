using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovieTracker.Evals;

public sealed class RunTitleSelector
{
    private readonly TmdbClient tmdbClient;

    private readonly Region[] regionOrder = new Region[] { Region.US, Region.IN, Region.SE };

    public RunTitleSelector(TmdbClient tmdbClient)
    {
        this.tmdbClient = tmdbClient;
    }

    public async Task<IReadOnlyList<RunTitle>> SelectAsync()
    {
        Dictionary<Region, int> chosenPages = new Dictionary<Region, int>();
        Dictionary<Region, IReadOnlyList<MovieSummary>> pageResults = new Dictionary<Region, IReadOnlyList<MovieSummary>>();

        foreach (Region region in this.regionOrder)
        {
            int page = Random.Shared.Next(1, 6);
            IReadOnlyList<MovieSummary> results = await this.tmdbClient.GetPopularAsync(region, page);
            if (results.Count < 1)
            {
                throw new InvalidOperationException($"TMDb /movie/popular (region={region}, page={page}) returned 0 entries; need at least 1");
            }

            chosenPages[region] = page;
            pageResults[region] = results;
        }

        List<RunTitle> picked = new List<RunTitle>();
        HashSet<int> takenIds = new HashSet<int>();

        for (int index = 0; index < this.regionOrder.Length; index++)
        {
            Region region = this.regionOrder[index];
            IReadOnlyList<MovieSummary> results = pageResults[region];
            MovieSummary chosen = this.PickOneDistinct(region, chosenPages[region], results, takenIds);

            picked.Add(new RunTitle(chosen.Id, chosen.Title, region, index + 1));
            takenIds.Add(chosen.Id);
        }

        return picked;
    }

    private MovieSummary PickOneDistinct(
        Region region,
        int page,
        IReadOnlyList<MovieSummary> results,
        HashSet<int> takenIds)
    {
        List<int> availableIndices = new List<int>();
        for (int index = 0; index < results.Count; index++)
        {
            if (!takenIds.Contains(results[index].Id))
            {
                availableIndices.Add(index);
            }
        }

        if (availableIndices.Count == 0)
        {
            throw new InvalidOperationException($"TMDb /movie/popular (region={region}, page={page}) has no entry not already taken by another region");
        }

        int pick = availableIndices[Random.Shared.Next(availableIndices.Count)];
        return results[pick];
    }
}
