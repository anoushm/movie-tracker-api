namespace MovieTracker.Api.Core;

public record AppError(string Code, string Message, int StatusCode = 400)
{
    public static AppError Validation(string message) => new("VALIDATION_ERROR", message, 400);
    public static AppError NotFound(string message) => new("NOT_FOUND", message, 404);
    public static AppError External(string message) => new("EXTERNAL_SERVICE_ERROR", message, 502);
    public static AppError Unauthorized(string message) => new("UNAUTHORIZED", message, 401);
    public static AppError Internal(string message) => new("INTERNAL_ERROR", message, 500);
}
