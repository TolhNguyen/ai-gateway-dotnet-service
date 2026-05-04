using AiGateway.Api.Application;
using AiGateway.Api.Contracts;
using AiGateway.Api.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;

namespace AiGateway.Api.Controllers;

[ApiController]
[Route("v1/admin/partners")]
public sealed class AdminPartnersController : ControllerBase
{
    private readonly AiConfigRepository _repository;
    private readonly AiConfigService _configService;

    public AdminPartnersController(AiConfigRepository repository, AiConfigService configService)
    {
        _repository = repository;
        _configService = configService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPartners()
    {
        return Ok(await _repository.GetPartnersAsync());
    }

    [HttpPost]
    public async Task<IActionResult> UpsertPartner([FromBody] UpsertPartnerRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpsertPartnerAsync(request);
        await _configService.InvalidateRoutesAsync(cancellationToken);
        return Ok(new { success = true, code = request.Code });
    }

    [HttpPatch("{partnerCode}/status")]
    public async Task<IActionResult> UpdateStatus(string partnerCode, [FromBody] UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        await _repository.UpdatePartnerStatusAsync(partnerCode, request.Status);
        await _configService.InvalidateRoutesAsync(cancellationToken);
        return Ok(new { success = true, code = partnerCode, status = request.Status });
    }
}
