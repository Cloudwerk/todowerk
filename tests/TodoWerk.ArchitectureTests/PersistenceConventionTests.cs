using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TodoWerk.Infrastructure.Persistence;
using Xunit;

namespace TodoWerk.ArchitectureTests;

/// <summary>
/// Conventions the persisted model has to hold to, checked against the model EF builds rather
/// than against the migration files — a mapping added without a configuration class would slip
/// past a file-level check. Nothing here opens a connection: the connection string is a
/// placeholder, present only because <c>UseSqlServer</c> insists on one.
/// </summary>
public sealed class PersistenceConventionTests
{
    private const string ModelOnlyConnectionString = "Server=model-only;Database=model-only";

    /// <summary>
    /// Entities whose rows belong to a tenant and to nobody in particular, and which therefore
    /// carry no user id. Named here one at a time rather than inferred, because "this row has no
    /// owner" is a claim about the domain and the whole point of the test below is that nobody gets
    /// to make it by accident.
    /// <para>
    /// <c>TenantConsentGrant</c> is the only member. An administrator approving TodoWerk approves it
    /// for the organisation, and TodoWerk deliberately does not record which administrator clicked —
    /// so there is no user for that row, and inventing one would be exactly the invented value the
    /// test exists to prevent.
    /// </para>
    /// </summary>
    private static readonly string[] TenantScopedEntities = ["TenantConsentGrant"];

    /// <summary>
    /// Every row says which tenant it belongs to, and which person — unless it is one of the
    /// tenant-scoped entities above. A migration that adds a discriminator to a populated table has
    /// to invent values for the rows already there, and for a per-person table there is no correct
    /// value to invent; cheaper to carry both columns from the first migration than to backfill them
    /// from Graph later.
    /// <para>
    /// Indexed tasks, Hashtag Occurrences, per-list sync state, and queued scans all carry both
    /// columns because of this test. The Tenant Member carries a nullable one, which is the same
    /// claim with erasure allowed for — a row that has been anonymised names no person on purpose.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPersistedEntity_CarriesTenantAndUser()
    {
        using var context = CreateContext();

        var mapped = context.Model.GetEntityTypes().Where(entityType => !entityType.IsOwned());

        Assert.All(mapped, entityType =>
        {
            Assert.NotNull(entityType.FindProperty("TenantId"));

            if (!TenantScopedEntities.Contains(entityType.ClrType.Name))
            {
                Assert.NotNull(entityType.FindProperty("UserId"));
            }
        });
    }

    /// <summary>
    /// Every column holding an id Microsoft Graph issued is compared byte for byte.
    /// Graph writes those ids as case-sensitive base64, and under the database's default
    /// collation two legitimately different tasks whose ids differ by one letter's case are one
    /// value — which the unique index over them reports as a duplicate key, wedging the list.
    /// <para>
    /// Checked against the model rather than against the columns, so the day another Graph id is
    /// added it is this test that says so rather than a live tenant.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryGraphIdColumn_IsBinaryCollated()
    {
        using var context = CreateContext();

        // The design-time model, because the runtime one drops what it cannot be asked at
        // runtime — a column's collation among it.
        var model = context.GetService<IDesignTimeModel>().Model;

        var graphIds = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.Name is "GraphTaskId" or "TaskListId")
            .ToList();

        Assert.NotEmpty(graphIds);
        Assert.All(graphIds, property => Assert.Equal("Latin1_General_100_BIN2", property.GetCollation()));
    }

    /// <summary>
    /// The Tenant Member record holds the tenant, the object id and two moments, and nothing else.
    /// <para>
    /// Asserted against the model rather than trusted to the class, because this is a promise made
    /// to the people it is about: no name, no email, no UPN, no address, no user agent, and no
    /// history between the two moments. A column added in passing would break the promise silently
    /// and PRIVACY.md would start being wrong, so the list is spelled out and the test fails on
    /// anything else — including on a rename, which is the point.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTenantMember_HoldsTheTenantTheObjectIdAndTwoMomentsAndNothingElse()
    {
        using var context = CreateContext();

        var member = context.Model.FindEntityType(typeof(Domain.Onboarding.TenantMember));

        Assert.NotNull(member);
        Assert.Equal(
            ["FirstSignedInAt", "Id", "LastSignedInAt", "TenantId", "UserId"],
            member.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The model and the checked-in migrations describe the same schema. Without this, a mapping
    /// change with no migration behind it is invisible: the build is green, every test passes
    /// against a database created from the migrations, and the mismatch surfaces as a missing
    /// column in production.
    /// </summary>
    [Fact]
    public void TheModel_HasNoChangesTheMigrationsDoNotCover()
    {
        using var context = CreateContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model has changed since the last migration. Run `dotnet ef migrations add <Name>` "
            + "(see CONTRIBUTING.md § Changing the schema).");
    }

    private static TodoWerkDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TodoWerkDbContext>()
            .UseSqlServer(ModelOnlyConnectionString)
            .Options);
}
