<!--
The pull request title becomes the squashed commit message.
Make it a valid Conventional Commit: feat: / fix: / docs: / chore: / ci: / refactor: / test:
-->

## What this changes

<!-- One or two sentences. The diff says what; say why. -->

## Related

<!-- Closes #123 / Part of milestone M1 / Implements ADR-0005 -->

Closes #

## How it was verified

<!-- Which tests, and anything you checked by hand that a test cannot cover. -->

- [ ] `dotnet test TodoWerk.slnx` passes locally
- [ ] `dotnet format TodoWerk.slnx --verify-no-changes` is clean
- [ ] `npm run typecheck` passes (if the client changed)
- [ ] Verified by hand against a real tenant (if sign-in or Graph behaviour changed)

## Checklist

- [ ] Branched from `main`, and the title is a valid Conventional Commit
- [ ] I have read [CONTRIBUTING.md](https://github.com/Cloudwerk/todowerk/blob/main/CONTRIBUTING.md) and signed the [CLA](https://github.com/Cloudwerk/todowerk/blob/main/CLA.md)
- [ ] Uses the vocabulary in [CONTEXT.md](https://github.com/Cloudwerk/todowerk/blob/main/CONTEXT.md) rather than inventing new terms
- [ ] Behavioural changes come with tests in the right suite (unit / architecture / integration)
- [ ] No new warnings — the build treats them as errors, and no suppressions were added to get it green
- [ ] No secrets, connection strings, or real tenant identifiers in the diff
- [ ] Contradicts no ADR in [docs/adr/](https://github.com/Cloudwerk/todowerk/tree/main/docs/adr), or explains below which one it revises and why
- [ ] `docs/status.md` and `docs/CHANGELOG.md` updated if this changes what works today

## Anything reviewers should push back on

<!-- Trade-offs you made, alternatives you rejected, parts you are unsure about.
     Naming them here gets you a better review than leaving them to be discovered. -->
