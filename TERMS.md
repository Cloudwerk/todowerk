# Terms of use

Last updated: 2026-09-22.

These are the terms on which you may use TodoWerk. They are short because TodoWerk is a small thing:
it reads the hashtags out of your own Microsoft To Do tasks, shows you what you have, and rewrites
them when you tell it to. Almost everything a longer document would say about accounts, payments,
content and third parties does not apply, and saying it anyway would bury the parts that do.

## Who this is between

This installation of TodoWerk is operated by **{{Operator}}**, called the Operator below. It is the
Operator you are agreeing with.

"You" is the person signing in. Where you sign in with a Microsoft work or school account, your
organisation is the one whose administrator can approve TodoWerk on your behalf, revoke it, and
decide whether you may use it at all. Both you and your organisation are bound by what follows, each
to the extent it concerns them.

By signing in you accept these terms. If you do not accept them, do not sign in. There is nothing
else you need to do, because TodoWerk holds nothing about you until you have.

## What TodoWerk does

It reads the titles of the tasks in your Microsoft To Do lists, extracts the hashtags in them, and
shows you a count of each one: how often it is used, across how many lists, when it was last
touched, and which ones look like mistakes. When you confirm a change, it writes those task titles
back with the hashtag's own text changed and the rest of the sentence untouched.

That is the whole of it. TodoWerk never creates, completes or deletes a task, never touches anybody
else's tasks, and never shows one person's hashtags to another.

## What it does not do

It is not a backup, and it is not a system of record. The copy TodoWerk holds of your task titles
exists to count hashtags and is discarded when you are forgotten. Microsoft To Do is where your
tasks live. TodoWerk is a lens on them.

It is not a place to keep anything. There is nothing to upload and nowhere to put a file.

## Who may use it, and what it costs

Anybody with a Microsoft work or school account that can sign in, subject to whatever their own
organisation allows. An administrator can approve TodoWerk for a whole organisation or refuse it,
and removing TodoWerk's permission in the Microsoft Entra admin center ends everybody's access to it
in that organisation.

Use of this installation is at no charge, and there is nothing to buy inside it. On your first
sign-in you receive a personal licence valid for one year from that day. When it ends, TodoWerk says
so, and the About page names whom to contact.

You are responsible for your own account. TodoWerk never sees your password and holds no credential
of yours other than the Microsoft Graph refresh token described in the
[privacy notice](PRIVACY.md).

## What you agree not to do

- Sign in as somebody else, or let somebody else use your session.
- Try to reach another person's tasks, hashtags or records, through the interface, the API or
  anything else.
- Automate against TodoWerk at a volume that degrades it for other people. There is a per-person
  request ceiling and it is deliberately wide. Meeting it repeatedly is a sign you are doing
  something this service is not for.
- Attempt to break, probe or circumvent its security. Reporting a vulnerability is welcome and has
  its own route, the [security policy](SECURITY.md), and doing that in good faith is not a breach of
  this clause.
- Use it to do something unlawful, or to help somebody else do so.

## Your data

What TodoWerk stores, how long it keeps each thing, and how to have all of it destroyed is set out
in the [privacy notice](PRIVACY.md), which forms part of these terms. The short version: your task
titles and the hashtags in them, a small record that you signed in, and your encrypted Microsoft
Graph refresh token. "Delete my data" sits beside "Sign out" and takes effect immediately, and
anything left untouched for {{DormancyWindowDays}} days is destroyed without being asked.

Your Microsoft To Do tasks are yours and stay where they are. Deleting your TodoWerk data does not
touch them.

## Undo is thirty days, and then it is not

A change can be undone as a whole for thirty days, because the journal that makes undo possible is
kept for thirty days and then destroyed. After that the rewrite is permanent as far as TodoWerk is
concerned. Preview the change before you confirm it. The exact task titles it would rewrite are
shown to you first, and that screen is the real safeguard.

## The source code is under a different licence

TodoWerk is free software under the GNU Affero General Public License v3.0, and that licence governs
the source code: your right to read it, run it, modify it and redistribute it. It is not these terms
and these terms are not it. Nothing here restricts what the AGPL grants you over the code, and
nothing the AGPL grants you is a right to use this particular installation. For that, these terms
are the ones that apply.

## Availability

TodoWerk is provided as it is, when it is available. The Operator does not promise any particular
level of availability under these terms unless it has separately agreed one with you in writing, and
may take the service down for maintenance, change how it works, or stop operating it altogether.

If the Operator stops operating this installation it will give you what notice it reasonably can, so
that you can take a copy of anything you want and have your data destroyed. Your tasks in Microsoft
To Do are unaffected either way.

## No warranty

To the extent the law allows, TodoWerk is provided without warranty of any kind. The Operator does
not warrant that it will be uninterrupted, that it will be free of defects, or that the hashtag
analysis it shows you is correct or complete. It is a tool that reads text and counts patterns in
it, and you should look at what a change is going to do before you confirm it.

## Liability

The Operator is liable without limit for death or personal injury, for damage caused intentionally
or by gross negligence, and for anything else the law does not permit it to exclude. Statutory
product liability is unaffected.

For breach of a material obligation, one whose fulfilment makes the proper performance of this
agreement possible at all and on which you may reasonably rely, the Operator is liable for the
foreseeable damage typical of this kind of agreement.

Beyond that, the Operator is not liable. In particular it is not liable for lost data, for lost
profit, or for the consequences of a task title being rewritten in a way you did not intend, where
the change was previewed to you and you confirmed it.

## Suspension and ending

You can stop using TodoWerk at any time, and you can have everything it holds about you destroyed
from inside the application. Neither needs a reason and neither needs to be asked for.

The Operator may suspend or end your access if you breach these terms, if your organisation's
administrator withdraws TodoWerk's permission, if your licence has ended, or if it stops operating
the service. Where the circumstances allow it, the Operator will say so first.

## Changes to these terms

These terms can change, because the software changes and a document that described an older version
of it would be worse than one that changes. The date at the top says when this version was
published. Material changes will be signposted in the application before they take effect where
that is practicable. If you keep using TodoWerk after a change, you accept it. If you do not accept
it, stop using TodoWerk and delete your data.

## Governing law

These terms are governed by the law of {{GoverningLaw}}, without regard to its conflict-of-laws
rules. Where you are a consumer, this does not deprive you of the protection of the mandatory law of
the country you live in.

## Asking about this

Questions about these terms go to {{OperatorContact}}. Security vulnerabilities go through the
[security policy](SECURITY.md) instead, not to this address and not to the issue tracker.
