using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace MovieTracker.Api.ChatSessions;

public class MoviceTrackerChatSession
{
    public string id { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public List<ChatMessage> ChatHistory { get; set; } = new();
    public string? FunnyFact { get; set; }
}
