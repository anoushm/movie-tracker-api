using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MovieTracker.Api.Core;

public static class ResultExtensions
{
    public static ActionResult ToActionResult(this AppError error)
    {
        ObjectResult result = new ObjectResult(new { error.Code, error.Message })
        {
            StatusCode = error.StatusCode
        };
        return result;
    }

    public static ActionResult<T> ToActionResult<T>(this Result<T> result, Func<T, ActionResult<T>> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value!);
        }
        return result.Error!.ToActionResult();
    }

    public static string UnwrapForAgent(this Result<string> result)
    {
        if (result.IsSuccess)
        {
            return result.Value!;
        }

        return JsonSerializer.Serialize(new
        {
            Error = true,
            result.Error!.Code,
            result.Error.Message
        });
    }

    public static async Task<string> UnwrapForAgentAsync(this Task<Result<string>> resultTask)
    {
        Result<string> result = await resultTask;
        return result.UnwrapForAgent();
    }
}
