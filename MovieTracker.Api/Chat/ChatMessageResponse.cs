using System.Collections.Generic;

namespace MovieTracker.Api.Chat;

public record ChatMessageResponse(string? FunnyFact, List<ChatMessage> Messages);
