using System.Text.Json.Serialization;

namespace MovieTracker.Api.Chat;

public class UserChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "user";

    public override string Text { get; }

    public UserChatMessage(string text)
    {
        Text = text;
    }
}
