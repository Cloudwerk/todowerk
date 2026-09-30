using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoWerk.Application.Onboarding;

namespace TodoWerk.Infrastructure.Onboarding;

/// <summary>
/// One pass of the retention rule: find the people who have not signed in for longer than the
/// dormancy window, and forget them.
/// <para>
/// A class of its own rather than a method on the worker, for the reason the scan scheduler's
/// <c>ScheduleDueSyncsAsync</c> is one: a test has to be able to run exactly one pass and look at
/// what happened, and a test that instead started a worker and waited would reach its verdict by
/// timing.
/// </para>
/// <para>
/// Restart-safe by construction rather than by a checkpoint. Erasure ends by anonymising the
/// membership row, and the query below only returns rows that still name somebody — so a person
/// already forgotten is never seen again, and a person half-forgotten is seen again and finished.
/// Nobody is erased twice and nobody is skipped.
/// </para>
/// </summary>
internal sealed class RetentionSweep(
    ITenantMemberStore members,
    PersonalDataEraser eraser,
    IOptions<OnboardingOptions> options,
    ILogger<RetentionSweep> logger)
{
    internal async Task<RetentionSweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var dormant = await members.FindDormantAsync(
            settings.DormancyWindow,
            settings.SweepBatchSize,
            cancellationToken);

        if (dormant.Count == 0)
        {
            return new RetentionSweepResult(0, 0);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Retention sweep found {Count} person(s) dormant for more than {DormancyDays} day(s).",
                dormant.Count,
                settings.DormancyWindow.TotalDays);
        }

        var forgotten = 0;

        // One scope, one context, for the whole batch. Every purge is an ExecuteDelete and tracks
        // nothing; the only tracked entity per person is the membership row being anonymised, and a
        // batch of those is small by configuration.
        foreach (var user in dormant)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A person the eraser could not finish keeps their identifiable row and is found again on
            // the next tick. It has already logged why, so this only has to not count them.
            if (await eraser.EraseAsync(user, cancellationToken))
            {
                forgotten++;
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Retention sweep forgot {Count} of {Found} dormant person(s).",
                forgotten,
                dormant.Count);
        }

        return new RetentionSweepResult(dormant.Count, forgotten);
    }
}

/// <summary>
/// What one pass did. Both numbers travel because the worker's drain decision needs
/// <paramref name="Found"/> — a full batch means there may be more — while a caller judging
/// progress wants <paramref name="Forgotten"/>. Deciding the drain on the second would let one
/// permanently-unfinishable person, sorted first by dormancy, make every batch come up one short
/// and stall the drain for everybody behind them.
/// </summary>
/// <param name="Found">How many dormant people the pass loaded.</param>
/// <param name="Forgotten">How many of them were fully erased and anonymised.</param>
internal sealed record RetentionSweepResult(int Found, int Forgotten);
