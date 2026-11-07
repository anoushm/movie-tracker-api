using System.Collections.Generic;
using MovieTracker.Api.Models;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace MovieTracker.Api.ChatSessions;

public class MovieTrackerChatSession
{
    public string id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public List<AIChatMessage> ChatHistory { get; set; } = new();
    public string? FunnyFact { get; set; }
    public Dictionary<string, MovieViewModel>? HydratedMovies { get; set; }
}
