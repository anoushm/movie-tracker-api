using System.Text.Json.Serialization;

namespace MovieTracker.Api.Chat;

[JsonPolymorphic(TypeDiscriminatorPropertyName = nameof(role))]
[JsonDerivedType(typeof(UserChatMessage), typeDiscriminator: "user")]
[JsonDerivedType(typeof(AssistantChatMessage), typeDiscriminator: "assistant")]
public abstract class ChatMessage
{
    public abstract string role { get; }
    public abstract string Text { get; }
}
