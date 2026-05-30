using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc;
using MovieTracker.Api.Core;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class AskController : ControllerBase
{
    private readonly AIAgent agent;
    private readonly ILogger<AskController> logger;

    public AskController(AIAgent agent, ILogger<AskController> logger)
    {
        this.agent = agent;
        this.logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<AskResponseDot>> Ask([FromBody] AskRequestDot request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return AppError.Validation("Question is required").ToActionResult();
        }

        try
        {
            AgentRunResponse response = await agent.RunAsync(request.Question);
            return Ok(new AskResponseDot { Answer = response.ToString() });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent request failed");
            return AppError.Internal("An unexpected error occurred").ToActionResult();
        }
    }
}
