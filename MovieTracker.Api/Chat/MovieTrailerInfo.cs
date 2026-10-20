using TMDbLib.Objects.General;

namespace MovieTracker.Api.Chat;

public record MovieTrailerInfo(
    string Key,
    string Name,
    string Site,
    string Type,
    bool Official,
    string YouTubeUrl,
    string EmbedUrl,
    string ThumbnailUrl
)
{
    public static MovieTrailerInfo? CreateFrom(Video? tmdbVideo)
    {
        if (tmdbVideo == null || tmdbVideo.Site != "YouTube")
        {
            return null;
        }

        return new MovieTrailerInfo(
            Key: tmdbVideo.Key,
            Name: tmdbVideo.Name,
            Site: tmdbVideo.Site,
            Type: tmdbVideo.Type,
            Official: tmdbVideo.Official,
            YouTubeUrl: $"https://www.youtube.com/watch?v={tmdbVideo.Key}",
            EmbedUrl: $"https://www.youtube.com/embed/{tmdbVideo.Key}",
            ThumbnailUrl: $"https://img.youtube.com/vi/{tmdbVideo.Key}/maxresdefault.jpg"
        );
    }
}
