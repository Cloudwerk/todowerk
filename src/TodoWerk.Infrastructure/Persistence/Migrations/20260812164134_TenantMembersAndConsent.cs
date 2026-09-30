using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoWerk.Infrastructure.Persistence.Migrations;

/// <summary>
/// The two tables the Onboarding module owns: who has used TodoWerk, and which tenants approved it
/// through TodoWerk's own admin-consent flow.
/// <para>
/// Purely additive, and safe against a database with rows in it: nothing existing is altered, no
/// column is backfilled, and both tables start empty on a deployment that already has users — which
/// means the statistics begin from the day this is applied rather than from the day somebody first
/// signed in. There is nothing to reconstruct them from: TodoWerk never recorded a sign-in before.
/// </para>
/// <para>
/// <c>TenantMembers.UserId</c> is nullable because erasure removes it in place, and both of its
/// filtered indexes exist because of that: <c>UX</c> would otherwise reject the second anonymised
/// row in a tenant (SQL Server treats NULLs as equal in a unique index), and the sweep's index would
/// otherwise carry every row it can never act on.
/// </para>
/// </summary>
public partial class TenantMembersAndConsent : Migration
{
    private static readonly string[] TenantUserColumns = ["TenantId", "UserId"];

    private static readonly string[] SignedInMoments = ["FirstSignedInAt", "LastSignedInAt"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantConsentGrants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantConsentGrants", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TenantMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                FirstSignedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastSignedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantMembers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_TenantConsentGrants_Tenant",
            table: "TenantConsentGrants",
            column: "TenantId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TenantMembers_LastSignedIn",
            table: "TenantMembers",
            column: "LastSignedInAt",
            filter: "[UserId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_TenantMembers_Tenant",
            table: "TenantMembers",
            column: "TenantId")
            .Annotation("SqlServer:Include", SignedInMoments);

        migrationBuilder.CreateIndex(
            name: "UX_TenantMembers_Tenant_User",
            table: "TenantMembers",
            columns: TenantUserColumns,
            unique: true,
            filter: "[UserId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TenantConsentGrants");

        migrationBuilder.DropTable(
            name: "TenantMembers");
    }
}
