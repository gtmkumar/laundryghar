using System.Net;
using System.Text.Json;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Exceptions;

namespace laundryghar.Utilities.Middlewares.ExceptionsMiddleware;

public class ExceptionHandler
{
    private const string UnauthorizedMarker = "UnAuthorized";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandler> _logger;

    public ExceptionHandler(RequestDelegate next, ILogger<ExceptionHandler> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (errorCode, httpStatus) = MapException(ex);

        _logger.LogError(
            ex,
            "Unhandled exception for {Method} {Path}",
            context.Request.Method,
            context.Request.Path.Value);

        if (context.Response.HasStarted)
        {
            // Response already flushed; nothing safe we can write.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)httpStatus;
        context.Response.ContentType = "application/json";

        var payload = new Response
        {
            Status = false,
            Message = new Message
            {
                ErrorTypeCode = errorCode,
                ErrorMessage = BuildErrors(ex),
                ResponseMessage = ex.InnerException?.Message ?? ex.Message
            }
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
    }

    private static (ErrorMessageEnum Code, HttpStatusCode Status) MapException(Exception ex) => ex switch
    {
        ValidationException      => (ErrorMessageEnum.ValidationFailed, HttpStatusCode.UnprocessableEntity),
        BusinessRuleException    => (ErrorMessageEnum.ValidationFailed, HttpStatusCode.UnprocessableEntity),
        ForbiddenException       => (ErrorMessageEnum.Forbidden,        HttpStatusCode.Forbidden),
        DbUpdateException        => (ErrorMessageEnum.BadRequest,       HttpStatusCode.BadRequest),
        UnauthorizedAccessException => (ErrorMessageEnum.UnAuthorized,  HttpStatusCode.Unauthorized),
        _ when string.Equals(ex.Message, UnauthorizedMarker, StringComparison.Ordinal)
            => (ErrorMessageEnum.UnAuthorized, HttpStatusCode.Unauthorized),
        _ => (ErrorMessageEnum.Error, HttpStatusCode.InternalServerError)
    };

    private static IReadOnlyDictionary<string, string[]>? BuildErrors(Exception ex) => ex switch
    {
        ValidationException validation => validation.ErrorsDictionary,
        DbUpdateException { InnerException: { } inner } dbEx => new Dictionary<string, string[]>
        {
            [dbEx.Message] = new[] { inner.Message }
        },
        _ => new Dictionary<string, string[]>
        {
            [ex.GetType().Name] = new[] { ex.InnerException?.Message ?? ex.Message }
        }
    };
}
