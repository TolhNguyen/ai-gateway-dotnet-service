using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/admin/models/{modelCode}/routes")]
public sealed class AdminRoutesController : ControllerBase
{
    private readonly AiConfigRepository _repository;
    private readonly AiConfigService _configService;

    public AdminRoutesController(AiConfigRepository repository, AiConfigService configService)
    {
        _repository = repository;
        _configService = configService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRoutes(string modelCode)
    {
        return Ok(await _repository.GetRoutesByModelAsync(modelCode));
    }

    [HttpPost]
    public async Task<IActionResult> UpsertRoute(string modelCode, [FromBody] UpsertModelRouteRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpsertModelRouteAsync(modelCode, request);
        await _configService.InvalidateModelAsync(modelCode, cancellationToken);
        return Ok(new
        {
            success = true,
            modelCode,
            partnerCode = request.PartnerCode,
            routeCode = string.IsNullOrWhiteSpace(request.RouteCode) ? "default" : request.RouteCode
        });
    }
}
