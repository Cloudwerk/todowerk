using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TodoWerk.IntegrationTests;

/// <summary>
/// One tick of a real background worker, on a host that was started without any.
/// <para>
/// Constructed here rather than left in the host, because the setup is the point: a worker started
/// with the host ticks the instant it starts, which is while the test is still writing the rows it
/// means to have ticked over. The workers are <c>internal</c> and this assembly can see them
/// (<c>TodoWerk.Infrastructure.csproj</c>), so a test can start one when it is ready and stop it
/// when it has what it came for.
/// </para>
/// <para>
/// One tick and no more, provided the caller puts that worker's poll interval far enough out that
/// the timer never fires again: the first tick runs immediately, and what the assertions read
/// afterwards is the state one drain left rather than a row a later tick has claimed again.
/// </para>
/// </summary>
internal static class WorkerTick
{
    /// <summary>How long to wait for <paramref name="settled"/>, which is a real scan or Change.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Starts <typeparamref name="TWorker"/>, waits until the database says the tick has done what
    /// the test is watching for, and stops it again.
    /// </summary>
    /// <param name="settled">
    /// What the caller is waiting for, asked repeatedly. It should cover every row the assertions
    /// will read — a row that is claimed and handed back within the tick is <em>Running</em> in
    /// between, and a wait that ended on the other rows alone would read it mid-flight.
    /// </param>
    /// <returns>False if the patience ran out, which is the caller's assertion to make.</returns>
    internal static async Task<bool> RunAsync<TWorker>(
        IServiceProvider services,
        Func<Task<bool>> settled,
        CancellationToken cancellationToken)
        where TWorker : BackgroundService
    {
        ArgumentNullException.ThrowIfNull(settled);

        using var worker = ActivatorUtilities.CreateInstance<TWorker>(services);
        var hosted = (IHostedService)worker;

        await hosted.StartAsync(cancellationToken);

        try
        {
            var deadline = DateTimeOffset.UtcNow + Patience;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await settled())
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            return false;
        }
        finally
        {
            // Not the test's token. Stopping cancels the tick, and both workers hand a deferred
            // row back without one precisely so that a worker stopped mid-tick still leaves the
            // queue as it found it — so awaiting this is what makes the assertions that follow
            // read a settled database rather than one a dying tick is still writing.
            await hosted.StopAsync(CancellationToken.None);
        }
    }
}
