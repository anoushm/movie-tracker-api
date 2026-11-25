using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace MovieTracker.Api.Chat;

#pragma warning disable MAAI001
public sealed class ChatContextReducer
{
    private const int MaxModelTurns = 12;
    private const int CollapsedToolResultChars = 160;
    private const int CollapsedArgumentChars = 60;
    private const int CollapsedResultNames = 6;

    private readonly IChatReducer reducer;

    public ChatContextReducer()
    {
        this.reducer = new PipelineCompactionStrategy(
        [
            new ToolResultCompactionStrategy(
                trigger: CompactionTriggers.HasToolCalls(),
                minimumPreservedGroups: 2)
            {
                ToolCallFormatter = this.SummariseToolCalls,
            },
            new SlidingWindowCompactionStrategy(
                trigger: CompactionTriggers.TurnsExceed(MaxModelTurns),
                minimumPreservedTurns: 4),
        ]).AsChatReducer();
    }

    public Task<IEnumerable<AIChatMessage>> ReduceAsync(IList<AIChatMessage> messages, CancellationToken cancellationToken)
    {
        return this.reducer.ReduceAsync(messages, cancellationToken);
    }

    private string SummariseToolCalls(CompactionMessageGroup group)
    {
        Dictionary<string, string> results = new Dictionary<string, string>();
        foreach (FunctionResultContent result in group.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>())
        {
            results[result.CallId] = result.Result?.ToString() ?? string.Empty;
        }

        List<string> lines = new List<string> { "[Earlier tool calls]" };
        foreach (FunctionCallContent call in group.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            string text = results.TryGetValue(call.CallId, out string? result) ? result : string.Empty;
            lines.Add($"{call.Name}({this.SummariseArguments(call.Arguments)}) -> {this.SummariseResult(text)}");
        }

        return string.Join("\n", lines);
    }

    private string SummariseArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", arguments
            .Where(argument => argument.Value is not null)
            .Select(argument =>
            {
                string value = argument.Value!.ToString() ?? string.Empty;
                if (value.Length > CollapsedArgumentChars)
                {
                    value = value[..CollapsedArgumentChars] + "...";
                }
                return $"{argument.Key}={value}";
            }));
    }

    private string SummariseResult(string text)
    {
        if (text.Length == 0)
        {
            return "(no result)";
        }

        try
        {
            if (text.TrimStart().StartsWith('['))
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    List<string?> names = document.RootElement.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.Object)
                        .Select(this.NameOf)
                        .Where(name => name is not null)
                        .ToList();

                    int total = document.RootElement.GetArrayLength();
                    string label = total == 1 ? "result" : "results";
                    if (total == 0)
                    {
                        return "0 results";
                    }

                    if (names.Count > 0)
                    {
                        string shown = string.Join(", ", names.Take(CollapsedResultNames));
                        string more = names.Count > CollapsedResultNames ? $", +{names.Count - CollapsedResultNames} more" : string.Empty;
                        return $"{total} {label}: {shown}{more}";
                    }

                    return $"{total} {label}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return text.Length <= CollapsedToolResultChars
            ? text
            : $"{text[..CollapsedToolResultChars]}... [{text.Length - CollapsedToolResultChars} more chars elided]";
    }

    private string? NameOf(JsonElement element)
    {
        foreach (string property in new[] { "MovieName", "Title", "PersonName", "Name", "GenreName" })
        {
            if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
#pragma warning restore MAAI001
