namespace MovieTracker.Evals.Tmdb;

public sealed record MovieCredits(
    string? DirectorName,
    int? LeadActorId,
    string? LeadActorName);
