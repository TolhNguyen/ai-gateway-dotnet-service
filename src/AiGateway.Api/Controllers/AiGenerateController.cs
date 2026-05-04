using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/ai")]
public sealed class AiGenerateController : ControllerBase
{
    private readonly AiGatewayService _service;

    public AiGenerateController(AiGatewayService service)
    {
        _service = service;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<GenerateAiResponse>> Generate(
        [FromBody] GenerateAiRequest request,
        CancellationToken cancellationToken)
    {
        var clientCode = Request.Headers.TryGetValue("X-AI-Client", out var c) ? c.ToString() : null;
        var apiKey = Request.Headers.TryGetValue("X-AI-Key", out var k) ? k.ToString() : null;

        var result = await _service.GenerateAsync(request, clientCode, apiKey, cancellationToken);

        if (!result.Success)
        {
            var status = result.ErrorType switch
            {
                "auth_error" => 401,
                "validation_error" => 400,
                "model_not_found" => 404,
                _ => 502
            };

            return StatusCode(status, result);
        }

        return Ok(result);
    }
}
