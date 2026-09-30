using Microsoft.Extensions.Logging;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Abstractions.Behaviors;

internal sealed class LoggingQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<LoggingQueryHandlerDecorator<TQuery, TResponse>> logger) : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    private static readonly string QueryName = typeof(TQuery).Name;

    public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Handling query {QueryName}", QueryName);
        }

        var result = await inner.HandleAsync(query, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Query {QueryName} failed with {ErrorCode}: {ErrorDescription}",
                QueryName,
                result.Error.Code,
                result.Error.Description);
        }

        return result;
    }
}
