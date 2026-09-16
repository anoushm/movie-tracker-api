using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MovieTracker.Api.Core
{
    public class CosmosSystemTextJsonSerializer : CosmosSerializer
    {
        private JsonSerializerOptions _serializerOptions;

        private static JsonSerializerOptions CreateDefaultOptions() => new(AIJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = null,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                AIJsonUtilities.DefaultOptions.TypeInfoResolver,
                new DefaultJsonTypeInfoResolver()),
        };

        public CosmosSystemTextJsonSerializer(JsonSerializerOptions serializerOptions = null)
        {
            _serializerOptions = serializerOptions ?? CreateDefaultOptions();
        }

        public override T FromStream<T>(Stream stream)
        {
            if (stream == null || stream.CanRead == false)
            {
                return default(T);
            }

            using (stream)
            {
                if (typeof(Stream).IsAssignableFrom(typeof(T)))
                {
                    return (T)(object)stream;
                }

                return JsonSerializer.Deserialize<T>(stream, _serializerOptions);
            }
        }

        public override Stream ToStream<T>(T input)
        {
            MemoryStream streamPayload = new MemoryStream();
            using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter(streamPayload, new JsonWriterOptions { Indented = true }))
            {
                JsonSerializer.Serialize(utf8JsonWriter, input, _serializerOptions);
            }
            streamPayload.Position = 0;
            return streamPayload;
        }
    }
}
