namespace MovieTracker.Api.Telemetry;

public sealed record ModelPricing(
    double InputPerMillion,
    double OutputPerMillion,
    double? CachedInputPerMillion);
