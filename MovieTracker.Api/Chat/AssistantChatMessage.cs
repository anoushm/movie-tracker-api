using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MovieTracker.Api.Chat;

public class AssistantChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "assistant";

    public override string Text { get; }

    public List<MovieViewModel> MovieList { get; set; } = new List<MovieViewModel>();

    public AssistantChatMessage(string text, List<MovieViewModel> movieList)
    {
        Text = text;
        MovieList = movieList;
    }
}
