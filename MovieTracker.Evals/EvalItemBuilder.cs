using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI;

namespace MovieTracker.Evals;

public sealed class EvalItemBuilder
{
    private readonly AgentClient agentClient;

    public EvalItemBuilder(AgentClient agentClient)
    {
        this.agentClient = agentClient;
    }

    public async Task<(CaseResult? PreMark, EvalItem? Item)> BuildAnchorAsync(
        PlotAnchorCase anchor,
        string expectedJson)
    {
        try
        {
            string chatId = await this.agentClient.StartChatAsync();
            AgentReply reply = await this.agentClient.AskAsync(chatId, anchor.Question);
            EvalItem item = this.BuildEvalItem(anchor.Question, reply, expectedJson);
            return (null, item);
        }
        catch (Exception ex)
        {
            return (new CaseResult(anchor.Id, "listIncludes", false, ex.Message), null);
        }
    }

    public async Task<(CaseResult? PreMark, EvalItem? Item)> BuildGeneratedAsync(
        GeneratedCase generated,
        string checkName)
    {
        try
        {
            string chatId = await this.agentClient.StartChatAsync();

            string graderQuestion;
            AgentReply reply;
            if (generated.Turns is not null && generated.Turns.Count > 0)
            {
                string lastReplyText = string.Empty;
                string? lastFunnyFact = null;
                IReadOnlyList<MovieListEntry> lastMovieList = Array.Empty<MovieListEntry>();
                foreach (string turnText in generated.Turns)
                {
                    AgentReply turnReply = await this.agentClient.AskAsync(chatId, turnText);
                    lastReplyText = turnReply.Answer;
                    lastFunnyFact = turnReply.FunnyFact;
                    lastMovieList = turnReply.MovieList;
                }

                reply = new AgentReply(lastReplyText, lastFunnyFact, lastMovieList);
                graderQuestion = generated.Turns[generated.Turns.Count - 1];
            }
            else
            {
                reply = await this.agentClient.AskAsync(chatId, generated.Question);
                graderQuestion = generated.Question;
            }

            EvalItem item = this.BuildEvalItem(graderQuestion, reply, generated.ExpectedJson);
            return (null, item);
        }
        catch (Exception ex)
        {
            return (new CaseResult(generated.Id, checkName, false, ex.Message), null);
        }
    }

    private EvalItem BuildEvalItem(string question, AgentReply reply, string? expectedJson)
    {
        string contextJson = JsonSerializer.Serialize(new ActualResponsePayload(reply.FunnyFact, reply.MovieList));
        return new EvalItem(question, reply.Answer)
        {
            Context = contextJson,
            ExpectedOutput = expectedJson,
        };
    }
}
