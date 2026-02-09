using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AskController : ControllerBase
    {
        private readonly AIAgent agent;

        public AskController(AIAgent agent)
        {
            this.agent = agent;
        }

        [HttpPost]
        public async Task<ActionResult<AskResponseDot>> Ask([FromBody] AskRequestDot request)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return BadRequest("Question is required.");
            }

            var response = await agent.RunAsync(request.Question);

            return Ok(new AskResponseDot { Answer = response.ToString() });
        }
    }
}
