﻿using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApplicationFlowSync.Errors
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            this.logger = logger;
        }
        //public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        //{
        //    logger.LogError(exception, "catch error: {Message}", exception.Message);

        //    var problemDetails = new ProblemDetails()
        //    {
        //        Status = StatusCodes.Status500InternalServerError,
        //        Title = "server error",
        //        Detail = exception.Message,
        //    };

        //    // 🟩 أضف هذا السطر قبل إرسال الرد
        //    httpContext.Response.ContentType = "application/json";
        //    httpContext.Response.StatusCode = problemDetails.Status.Value;
        //    await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        //    return true;
        //}

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            logger.LogError(exception, "catch error: {Message}", exception.Message);

            var statusCode = exception is AppException appException
                ? appException.StatusCode
                : StatusCodes.Status500InternalServerError;

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode == 500 ? "server error" : "error",
                Detail = exception.Message
            };

            httpContext.Response.ContentType = "application/json";
            httpContext.Response.StatusCode = statusCode;
            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

            return true;
        }
    }
}
