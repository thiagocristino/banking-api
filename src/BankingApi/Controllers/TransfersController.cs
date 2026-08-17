using System.Security.Claims;
using BankingApi.DTOs;
using BankingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankingApi.Controllers;

[ApiController]
[Authorize]
[Route("transfers")]
public class TransfersController : ControllerBase
{
    private readonly TransferService _transferService;

    public TransfersController(
        TransferService transferService)
    {
        _transferService = transferService;
    }

    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Create(
        TransferRequest request)
    {
        var accountIdClaim =
            User.FindFirstValue("account_id");

        if (string.IsNullOrWhiteSpace(accountIdClaim))
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(
            accountIdClaim,
            out var accountId))
        {
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue(
            "Idempotency-Key",
            out var idempotencyKey))
        {
            return BadRequest(new
            {
                code = "IDEMPOTENCY_KEY_REQUIRED",
                message =
                    "The Idempotency-Key header is required."
            });
        }

        var result =
            await _transferService.CreateAsync(
                accountId,
                request,
                idempotencyKey.ToString());

        return Ok(result);
    }
}