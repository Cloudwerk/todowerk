using TodoWerk.Application.Abstractions.Authentication;
using TodoWerk.Application.Abstractions.Messaging;
using TodoWerk.SharedKernel;

namespace TodoWerk.Application.Onboarding.EraseSelf;

internal sealed class EraseSelfCommandHandler(ICurrentUser currentUser, PersonalDataEraser eraser)
    : ICommandHandler<EraseSelfCommand>
{
    public async Task<Result> HandleAsync(EraseSelfCommand command, CancellationToken cancellationToken)
    {
        var user = IndexUser.From(currentUser);

        if (user is null)
        {
            return Result.Failure(OnboardingErrors.NotSignedIn);
        }

        // Not signed out on a partial erasure, deliberately: the person is the one who can try again,
        // and signing them out of a job that did not finish would take that away from them.
        return await eraser.EraseAsync(user, cancellationToken)
            ? Result.Success()
            : Result.Failure(OnboardingErrors.ErasureIncomplete);
    }
}
