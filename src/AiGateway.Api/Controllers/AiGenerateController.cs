using AiGateway.Api.Application.AI;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/ai")]
[Authorize]
public sealed class AiGenerateController : ControllerBase
{
    private readonly AiGatewayService _service;
    private readonly ICurrentUser _current;

    public AiGenerateController(AiGatewayService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateAiRequest req, CancellationToken ct)
    {
        if (_current.UserId is not long uid) return Unauthorized();

        var resp = await _service.GenerateAsync(uid, req, HttpContext, ct);

        if (!resp.Success)
        {
            var status = resp.ErrorType switch
            {
                "permission_error" => StatusCodes.Status403Forbidden,
                "auth_error"       => StatusCodes.Status401Unauthorized,
                "rate_limit"       => StatusCodes.Status429TooManyRequests,
                "quota_exceeded"   => StatusCodes.Status429TooManyRequests,
                "timeout"          => StatusCodes.Status504GatewayTimeout,
                _                  => StatusCodes.Status502BadGateway
            };
            return StatusCode(status, resp);
        }
        return Ok(resp);
    }
}
