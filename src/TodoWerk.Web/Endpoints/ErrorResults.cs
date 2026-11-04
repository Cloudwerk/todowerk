using TodoWerk.SharedKernel;

namespace TodoWerk.Web.Endpoints;

/// <summary>Single place where a domain <see cref="Error"/> becomes an HTTP problem response.</summary>
internal static class ErrorResults
{
    /// <param name="extensions">
    /// Anything else this particular refusal carries, beside the <c>code</c> every problem here
    /// has. Two callers use it, for the same reason: each refusal is the only thing its card is
    /// given, and what the card needs is something a domain error cannot reach. The licence gate
    /// puts the operator's contact on the ended card's problem, because that card has nowhere else
    /// to send somebody and the address is a Web-layer setting; the Teams exchange puts an Approval
    /// Link on the consent card's, because the link is built from the request's own host.
    /// </param>
    public static IResult ToProblem(this Error error, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var (statusCode, title) = ResponseFor(error.Type);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };

        if (extensions is not null)
        {
            foreach (var extension in extensions)
            {
                payload[extension.Key] = extension.Value;
            }
        }

        return Results.Problem(
            title: title,
            detail: error.Description,
            statusCode: statusCode,
            extensions: payload);
    }

    private static (int StatusCode, string Title) ResponseFor(ErrorType type) => type switch
    {
        ErrorType.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
        ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Not authorized"),
        ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Not allowed"),
        ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
        ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
        _ => (StatusCodes.Status500InternalServerError, "Request failed"),
    };
}
