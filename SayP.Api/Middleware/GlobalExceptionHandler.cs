using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

namespace SayP.Api.Middleware;

/// <summary>
/// Global exception handler middleware
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = CreateProblemDetails(httpContext, exception);

        httpContext.Response.StatusCode = problemDetails.Status ?? (int)HttpStatusCode.InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private ProblemDetails CreateProblemDetails(HttpContext context, Exception exception)
    {
        var statusCode = GetStatusCode(exception);
        var title = GetTitle(exception);
        var detail = GetDetail(exception);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };

        // Add exception details in development
        if (_environment.IsDevelopment())
        {
            problemDetails.Extensions["exception"] = exception.GetType().Name;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
            problemDetails.Extensions["innerException"] = exception.InnerException?.Message;
        }

        // Add trace ID
        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        return problemDetails;
    }

    private int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => (int)HttpStatusCode.BadRequest,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            InvalidOperationException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            NotImplementedException => (int)HttpStatusCode.NotImplemented,
            TimeoutException => (int)HttpStatusCode.RequestTimeout,
            _ => (int)HttpStatusCode.InternalServerError
        };
    }

    private string GetTitle(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => "Bad Request",
            ArgumentException => "Bad Request",
            InvalidOperationException => "Bad Request",
            UnauthorizedAccessException => "Unauthorized",
            NotImplementedException => "Not Implemented",
            TimeoutException => "Request Timeout",
            _ => "Internal Server Error"
        };
    }

    private string GetDetail(Exception exception)
    {
        // Return user-friendly messages in production
        if (!_environment.IsDevelopment())
        {
            return exception switch
            {
                ArgumentNullException => "A required parameter was missing.",
                ArgumentException => "Invalid request parameters.",
                InvalidOperationException => "The requested operation could not be completed.",
                UnauthorizedAccessException => "You are not authorized to perform this action.",
                NotImplementedException => "This feature is not yet implemented.",
                TimeoutException => "The request timed out. Please try again.",
                _ => "An unexpected error occurred. Please try again later."
            };
        }

        // Return detailed messages in development
        return exception.Message;
    }
}
