using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Models;
using OpenTelemetry.Trace;

namespace MovieTracker.Api.Chat;

public static class ChatTurnEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapGet("/api/chat/start", async (IChatPipeline pipeline, Tracer tracer, ILogger<Program> logger) =>
        {
            using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-start");
            try
            {
                IChatPipeline.StartOutcome outcome = await pipeline.StartSessionAsync();
                return Results.Ok(new { chatId = outcome.ChatId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Chat-Start session creation failed.");
                return Results.Json(new { error = "Session creation failed." }, statusCode: 503);
            }
        })
            .WithName("ChatStart");

        app.MapPost("/api/chat/{chatId}/ask", async (
            string chatId,
            HttpRequest request,
            IChatPipeline pipeline,
            ILogger<ChatMessageResponse> logger,
            Tracer tracer) =>
        {
            try
            {
                using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-ask");
                if (!request.HasJsonContentType())
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                Ask? ask;
                try
                {
                    ask = await request.ReadFromJsonAsync<Ask>();
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                if (ask is null)
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                IChatPipeline.AskOutcome outcome = await pipeline.AskAsync(chatId, ask.Input);
                return outcome switch
                {
                    IChatPipeline.AskOutcome.NotFound => Results.NotFound(new { error = "Chat not found" }),
                    IChatPipeline.AskOutcome.Ok ok => Results.Ok(new ChatMessageResponse(ok.FunnyFact, ok.Messages)),
                    _ => Results.Json(new { error = "Chat request failed." }, statusCode: 503),
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Chat-Ask failed for session {ChatId}", chatId);
                return Results.Json(new { error = "Chat request failed." }, statusCode: 503);
            }
        })
            .WithName("ChatAsk");

        app.MapPost("/api/chat/{chatId}/ask/v2", async (
            string chatId,
            HttpRequest request,
            IChatPipeline pipeline,
            ILogger<ChatTurnResponse> logger,
            Tracer tracer) =>
        {
            try
            {
                using TelemetrySpan activity = tracer.StartActiveSpan("movie-tracker-api.chat-ask-v2");
                if (!request.HasJsonContentType())
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                Ask? ask;
                try
                {
                    ask = await request.ReadFromJsonAsync<Ask>();
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                if (ask is null)
                {
                    return Results.BadRequest(new { error = "Invalid request, missing ask object" });
                }

                IChatPipeline.AskV2Outcome outcome = await pipeline.AskV2Async(chatId, ask.Input);
                return outcome switch
                {
                    IChatPipeline.AskV2Outcome.NotFound => Results.NotFound(new { error = "Chat not found" }),
                    IChatPipeline.AskV2Outcome.Ok ok => Results.Ok(new ChatTurnResponse(ok.FunnyFact, ok.Turn)),
                    _ => Results.Json(new { error = "Chat request failed." }, statusCode: 503),
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Chat-Ask-V2 failed for session {ChatId}", chatId);
                return Results.Json(new { error = "Chat request failed." }, statusCode: 503);
            }
        })
            .WithName("ChatAskV2");
    }
}
