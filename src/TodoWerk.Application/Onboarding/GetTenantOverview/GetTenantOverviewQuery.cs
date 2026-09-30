using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Onboarding.GetTenantOverview;

/// <summary>
/// What this organisation's use of TodoWerk looks like from the inside. No parameters: there is
/// nothing to sort, filter or page, and the tenant is the one the caller signed in to — never one
/// they could name.
/// </summary>
public sealed record GetTenantOverviewQuery : IQuery<TenantOverviewDto>;
