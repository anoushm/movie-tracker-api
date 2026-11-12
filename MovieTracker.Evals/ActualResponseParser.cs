using System.Text.Json;

namespace MovieTracker.Evals;

public static class ActualResponseParser
{
    public static ActualResponsePayload? Parse(string? context)
    {
        if (string.IsNullOrEmpty(context))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ActualResponsePayload>(context);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
