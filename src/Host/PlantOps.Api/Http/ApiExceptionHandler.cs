using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PlantOps.SharedKernel;

namespace PlantOps.Api.Http;

// SharedKernel exceptions are translated: their messages are written for API callers. Returning false for
// everything else lets the default handler emit a generic 500 without leaking details.
// BadHttpRequestException (unparseable JSON, unbindable query value) must be mapped here too: the exception
// handler middleware would otherwise report it as a 500 even though it is the caller's mistake.
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var detail = exception.Message;
        var (status, title) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            DomainException => (StatusCodes.Status400BadRequest, "Business rule violated"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (0, null),
        };

        if (title is null)
        {
            return false;
        }

        if (exception is BadHttpRequestException)
        {
            // The framework's message names internal CLR types; callers get a generic one (details are logged).
            detail = "The request was malformed or contained an invalid value.";
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
