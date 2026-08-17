using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Exceptions;

public class ConcurrencyExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ConcurrencyExceptionHandler> _logger;

    public ConcurrencyExceptionHandler(
        ILogger<ConcurrencyExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        _logger.LogWarning(
            exception,
            "A concurrency conflict occurred while updating the database.");

        httpContext.Response.StatusCode =
            StatusCodes.Status409Conflict;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                code = "CONCURRENT_MODIFICATION",
                message =
                    "One of the accounts was modified by another transaction. Please retry."
            },
            cancellationToken);

        return true;
    }
}