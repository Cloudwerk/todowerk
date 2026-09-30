using TodoWerk.Application.Abstractions.Messaging;

namespace TodoWerk.Application.Onboarding.EraseSelf;

/// <summary>
/// Forget me. It names nobody: whose data is destroyed is read off the request's own session, so
/// there is no shape of this command that could erase somebody else.
/// </summary>
public sealed record EraseSelfCommand : ICommand;
