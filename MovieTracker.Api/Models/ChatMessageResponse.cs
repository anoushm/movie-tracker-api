using System.Collections.Generic;

namespace MovieTracker.Api.Models;

public record ChatMessageResponse(string? FunnyFact, List<ChatMessage> Messages);
