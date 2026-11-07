using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MovieTracker.Api.Models;

public class AssistantChatMessage : ChatMessage
{
    [JsonIgnore]
    public override string role => "assistant";

    public override string Text { get; }

    public List<MovieViewModel> MovieList { get; set; } = new List<MovieViewModel>();

    public AssistantChatMessage(string text, List<MovieViewModel> movieList)
    {
        this.Text = text;
        this.MovieList = movieList;
    }
}
