using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Chat;

public interface IChatPipeline
{
    Task<IChatPipeline.StartOutcome> StartSessionAsync();

    Task<IChatPipeline.AskOutcome> AskAsync(string chatId, string input);

    Task<IChatPipeline.AskV2Outcome> AskV2Async(string chatId, string input);

    public sealed record StartOutcome(string ChatId);

    public abstract record AskOutcome
    {
        public sealed record Ok(string? FunnyFact, List<ChatMessage> Messages) : AskOutcome;

        public sealed record NotFound : AskOutcome;
    }

    public abstract record AskV2Outcome
    {
        public sealed record Ok(string? FunnyFact, ChatMessage Turn) : AskV2Outcome;

        public sealed record NotFound : AskV2Outcome;
    }
}
