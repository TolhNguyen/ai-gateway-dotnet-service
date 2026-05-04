using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
public sealed class AdminAccountsController : ControllerBase
{
    private readonly AiConfigRepository _repository;
    private readonly AiConfigService _configService;
    private readonly ISecretProtector _secretProtector;

    public AdminAccountsController(
        AiConfigRepository repository,
        AiConfigService configService,
        ISecretProtector secretProtector)
    {
        _repository = repository;
        _configService = configService;
        _secretProtector = secretProtector;
    }

    [HttpGet("v1/admin/partners/{partnerCode}/accounts")]
    public async Task<IActionResult> GetAccounts(string partnerCode)
    {
        var accounts = await _repository.GetAccountsByPartnerAsync(partnerCode);
        return Ok(accounts.Select(x => x with { ApiKeyEnc = x.ApiKeyEnc is null ? null : "***" }));
    }

    [HttpPost("v1/admin/partners/{partnerCode}/accounts")]
    public async Task<IActionResult> UpsertAccount(string partnerCode, [FromBody] UpsertAccountRequest request, CancellationToken cancellationToken)
    {
        var encryptedKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? null
            : _secretProtector.Protect(request.ApiKey);

        await _repository.UpsertAccountAsync(partnerCode, request, encryptedKey);
        await _configService.InvalidateRoutesAsync(cancellationToken);
        return Ok(new { success = true, partnerCode, code = request.Code });
    }

    [HttpPatch("v1/admin/accounts/{accountCode}/status")]
    public async Task<IActionResult> UpdateStatus(string accountCode, [FromBody] UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpdateAccountStatusAsync(accountCode, request.Status);
        await _configService.InvalidateRoutesAsync(cancellationToken);
        return Ok(new { success = true, code = accountCode, status = request.Status });
    }
}
