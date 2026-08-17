using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BankingApi.Exceptions;

public class BusinessExceptionHandler
    : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public BusinessExceptionHandler(
        IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BusinessException businessException)
        {
            return false;
        }

        httpContext.Response.StatusCode = businessException.StatusCode;

        var problemDetails = new ProblemDetails
        {
            Status = businessException.StatusCode,
            Title = "Business rule violation",
            Detail = businessException.Message,
            Type = $"https://httpstatuses.com/{businessException.StatusCode}"
        };

        problemDetails.Extensions["code"] =
            businessException.Code;

        return await _problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            });
    }
}