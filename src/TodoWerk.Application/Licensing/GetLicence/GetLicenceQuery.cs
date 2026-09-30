using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Licensing.GetLicence;

/// <summary>
/// What the signed-in person is licensed under. No parameters: the person is the one who signed
/// in, never one they could name, and there is nothing to sort, filter or page.
/// </summary>
public sealed record GetLicenceQuery : IQuery<LicenceDto>;
