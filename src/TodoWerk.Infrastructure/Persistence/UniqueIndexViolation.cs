using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TodoWerk.Infrastructure.Persistence;

/// <summary>
/// Whether a failed save lost a race for a unique index, as opposed to failing for any of the other
/// reasons a save fails.
/// <para>
/// The distinction matters wherever "somebody else got there first" is a success. A blanket
/// <c>catch (DbUpdateException)</c> in those places swallows a deadlock, a timeout and a constraint
/// nobody was racing for, and then reports the write as having happened — which is the one thing the
/// caller must not be told wrongly.
/// </para>
/// </summary>
internal static class UniqueIndexViolation
{
    /// <summary>Duplicate key in a unique index, and in a unique constraint. SQL Server's own numbers.</summary>
    private const int DuplicateKeyInIndex = 2601;

    private const int DuplicateKeyInConstraint = 2627;

    internal static bool CausedBy(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKeyInIndex or DuplicateKeyInConstraint };
}
