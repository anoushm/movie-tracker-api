using System.Text.Json.Serialization;

namespace MovieTracker.Api.Models;

public class UserChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "user";

    public override string Text { get; }

    public UserChatMessage(string text)
    {
        this.Text = text;
    }
}
