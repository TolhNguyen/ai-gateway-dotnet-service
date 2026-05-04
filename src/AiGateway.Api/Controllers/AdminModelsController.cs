using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/admin/models")]
public sealed class AdminModelsController : ControllerBase
{
    private readonly AiConfigRepository _repository;
    private readonly AiConfigService _configService;

    public AdminModelsController(AiConfigRepository repository, AiConfigService configService)
    {
        _repository = repository;
        _configService = configService;
    }

    [HttpGet]
    public async Task<IActionResult> GetModels()
    {
        return Ok(await _repository.GetModelsAsync());
    }

    [HttpPost]
    public async Task<IActionResult> UpsertModel([FromBody] UpsertModelRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpsertModelAsync(request);
        await _configService.InvalidateModelAsync(request.Code, cancellationToken);
        return Ok(new { success = true, code = request.Code });
    }

    [HttpPatch("{modelCode}/status")]
    public async Task<IActionResult> UpdateStatus(string modelCode, [FromBody] UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpdateModelStatusAsync(modelCode, request.Status);
        await _configService.InvalidateModelAsync(modelCode, cancellationToken);
        return Ok(new { success = true, code = modelCode, status = request.Status });
    }
}
