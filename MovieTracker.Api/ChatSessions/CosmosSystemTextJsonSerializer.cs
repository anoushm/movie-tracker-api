using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;

namespace MovieTracker.Api.ChatSessions;

public class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions serializerOptions;

    private static JsonSerializerOptions CreateDefaultOptions() => new(AIJsonUtilities.DefaultOptions)
    {
        PropertyNamingPolicy = null,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            AIJsonUtilities.DefaultOptions.TypeInfoResolver,
            new DefaultJsonTypeInfoResolver()),
    };

    public CosmosSystemTextJsonSerializer(JsonSerializerOptions? serializerOptions = null)
    {
        this.serializerOptions = serializerOptions ?? CreateDefaultOptions();
    }

    public override T FromStream<T>(Stream stream)
    {
        if (stream == null || stream.CanRead == false)
        {
            return default(T)!;
        }

        using (stream)
        {
            if (typeof(Stream).IsAssignableFrom(typeof(T)))
            {
                return (T)(object)stream;
            }

            return JsonSerializer.Deserialize<T>(stream, this.serializerOptions)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        MemoryStream streamPayload = new MemoryStream();
        using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(streamPayload, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(utf8JsonWriter, input, this.serializerOptions);
        }
        streamPayload.Position = 0;
        return streamPayload;
    }
}
