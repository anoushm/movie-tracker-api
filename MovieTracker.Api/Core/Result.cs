namespace MovieTracker.Api.Core;

public readonly struct Result<T>
{
    public T? Value { get; }
    public AppError? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(AppError error) => new(default, error);

    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> next)
    {
        if (IsFailure)
        {
            return Result<TNew>.Failure(Error!);
        }
        return next(Value!);
    }

    public async Task<Result<TNew>> BindAsync<TNew>(Func<T, Task<Result<TNew>>> next)
    {
        if (IsFailure)
        {
            return Result<TNew>.Failure(Error!);
        }
        return await next(Value!);
    }

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<AppError, TResult> onFailure)
    {
        if (IsSuccess)
        {
            return onSuccess(Value!);
        }
        return onFailure(Error!);
    }

    public async Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> onSuccess, Func<AppError, Task<TResult>> onFailure)
    {
        if (IsSuccess)
        {
            return await onSuccess(Value!);
        }
        return await onFailure(Error!);
    }
}
