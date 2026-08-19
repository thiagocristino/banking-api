using System.Security.Claims;
using BankingApi.DTOs;
using BankingApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankingApi.Controllers;

[ApiController]
[Route("accounts")]
public class AccountsController : ControllerBase
{
    private readonly AccountService _accountService;

    public AccountsController(AccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(
        CreateAccountRequest request)
    {
        var result = await _accountService.CreateAsync(request);

        return Created(
            $"/accounts/{result.Id}",
            result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> Me()
    {
        var accountIdClaim = User.FindFirstValue("account_id");

        if (string.IsNullOrEmpty(accountIdClaim))
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(accountIdClaim, out var accountId))
        {
            return Unauthorized();
        }

        var result = await _accountService.GetByIdAsync(accountId);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [Authorize]
    [HttpPost("me/deposit")]
    public async Task<ActionResult<DepositResponse>> Deposit(
        DepositRequest request)
    {
        var accountIdClaim = User.FindFirstValue("account_id");

        if (string.IsNullOrEmpty(accountIdClaim))
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(accountIdClaim, out var accountId))
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
                message = "The Idempotency-Key header is required."
            });
        }

        var result = await _accountService.DepositAsync(
            accountId,
            request,
            idempotencyKey.ToString());

        return Ok(result);
    }

    [Authorize]
    [HttpGet("me/statement")]
    public async Task<ActionResult<StatementResponse>> Statement(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var accountIdClaim = User.FindFirstValue("account_id");

        if (string.IsNullOrEmpty(accountIdClaim))
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(accountIdClaim, out var accountId))
        {
            return Unauthorized();
        }

        if (page < 1)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGE",
                message = "Page must be greater than or equal to 1."
            });
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGE_SIZE",
                message = "PageSize must be between 1 and 100."
            });
        }

        var result = await _accountService.GetStatementAsync(
            accountId,
            startDate,
            endDate,
            page,
            pageSize);

        return Ok(result);
    }
}