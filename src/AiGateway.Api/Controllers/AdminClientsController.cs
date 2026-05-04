using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/admin/clients")]
public sealed class AdminClientsController : ControllerBase
{
    private readonly AiConfigRepository _repository;
    private readonly AiConfigService _configService;
    private readonly ApiKeyHasher _apiKeyHasher;

    public AdminClientsController(
        AiConfigRepository repository,
        AiConfigService configService,
        ApiKeyHasher apiKeyHasher)
    {
        _repository = repository;
        _configService = configService;
        _apiKeyHasher = apiKeyHasher;
    }

    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var clients = await _repository.GetClientsAsync();
        return Ok(clients.Select(x => x with { ApiKeyHash = x.ApiKeyHash is null ? null : "***" }));
    }

    [HttpPost]
    public async Task<IActionResult> UpsertClient([FromBody] UpsertClientRequest request, CancellationToken cancellationToken)
    {
        var apiKeyHash = string.IsNullOrWhiteSpace(request.ApiKey)
            ? null
            : _apiKeyHasher.Hash(request.ApiKey);

        await _repository.UpsertClientAsync(request, apiKeyHash);
        await _configService.InvalidateClientAsync(request.Code, cancellationToken);
        return Ok(new { success = true, code = request.Code });
    }

    [HttpPatch("{clientCode}/status")]
    public async Task<IActionResult> UpdateStatus(string clientCode, [FromBody] UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpdateClientStatusAsync(clientCode, request.Status);
        await _configService.InvalidateClientAsync(clientCode, cancellationToken);
        return Ok(new { success = true, code = clientCode, status = request.Status });
    }
}
