# TodoWerk for administrators

This page is for the person deciding whether TodoWerk may be used in their organisation, and for
the one who then makes it available. It says what TodoWerk asks for, what approving it does and does
not grant, what it stores about a person, what people in an organisation can see about each other,
how somebody is forgotten, how to revoke it, and how to roll it out. It is served by the Hosted
Service it describes. The numbers on it are the service's own settings, not a document's memory of
them.

The [privacy notice](../PRIVACY.md) is the fuller account and forms part of the
[terms of use](../TERMS.md). This page replaces neither. {{GuideLine}}

## What TodoWerk is, and what it reaches

TodoWerk reads the hashtags out of a person's own Microsoft To Do tasks, shows them what they have,
and rewrites task titles when that person confirms a change. It works with the signed-in person's
account and reaches that person's tasks and nobody else's. There is no surface in it that shows one
person's hashtags to another, none that changes a colleague's tasks, and none that counts hashtags
across people. It never creates, completes or deletes a task.

It runs in a browser and as a personal tab in Microsoft Teams, in Outlook and in the Microsoft 365
app. Work or school accounts only. Consumer Microsoft accounts are not supported.

## What consent is asked for

One delegated permission on Microsoft Graph, **`Tasks.ReadWrite`**. It reads the person's own task
titles to build the index, and writes them back when that person confirms a rename or a merge. With
it come the OpenID Connect scopes every sign-in needs: `openid`, `profile` and `offline_access`.
There is no read-only mode. The background worker that performs a confirmed change has no browser to
prompt through, so the write scope is asked for once, at sign-in, of everybody.

*Delegated* is the word that matters. A delegated permission acts as the signed-in person, with a
token issued to that person, and reaches only what that person can reach. TodoWerk takes **no
application permission** of any kind.

### What approving it for the organisation does

By default each person is asked to consent the first time they sign in. In a tenant whose policy
withholds user consent, they cannot. An administrator can approve the same delegated grant once, on
behalf of everybody, and from then on nobody in the organisation is prompted. TodoWerk offers the
route from inside the product. **Your organisation → Approve for the organisation** takes an
administrator to Microsoft's admin-consent page for exactly the permission above.

That changes **who is asked, and nothing else**. After the approval:

- TodoWerk still acts as each person, with that person's own token.
- It still holds no application permission and no directory permission. It cannot read your users,
  your groups or your sign-in logs. It has no credential of its own that could reach a mailbox.
- It still reaches no mailbox whose owner has not signed in. Somebody who never opens TodoWerk is
  not "excluded by configuration". They are unreachable, because reaching them would need a token
  issued to them.
- Nobody becomes an administrator inside TodoWerk. There is no admin role, no admin screen and no
  way to see or change another person's hashtags. That holds for the person who approved it, too.

The Microsoft consent dialog names the same permission and nothing wider. If it names more, you are
looking at a different application.

TodoWerk records that an approval was made *through its own page*. An approval made in the Microsoft
Entra admin center instead is invisible to it. The invitation stays on screen, and approving there
again is harmless.

## What TodoWerk stores about a person

Two kinds of thing, kept for different reasons.

**A record that the person uses it**, kept so that the organisation can see its own numbers. It
holds the organisation's tenant id, the person's Entra ID object id, when they first signed in and
when they last did. Nothing else. No name, no email address, no user principal name, no IP address,
no device, and no history of visits between the first and the last. It is written when a person
signs in and never by anything running in the background.

**What using the product leaves behind**, kept because the product is made of it:

| What | How long |
| --- | --- |
| The person's task titles, one row per task, and the hashtags extracted from them | Until the person is forgotten, below. Kept current in the background while the person is using TodoWerk, as the paragraph below defines it. A task deleted in To Do leaves the index at the next sync |
| Which lists were read, and how far each read got | The same |
| Each confirmed change, its per-task plan, and its journal. The journal holds the title read immediately before every write and the title written | Kept for {{ChangeRetentionDays}} days after the change finished. The journal is what makes a change undoable, and undo is offered for the same {{ChangeRetentionDays}} days |
| Each marker rule the person created: an emoji, the hashtag it is about, and its place in their list | Until the person is forgotten, below. A rule is something they wrote down rather than something TodoWerk observed, and nothing deletes it on its own. A rule the person deletes is kept and marked deleted rather than removed. Its emoji is still at the front of the tasks it was applied to, and TodoWerk has to go on recognising it there. Such a row is shown to the person under their own markers. It is destroyed once they remove that emoji from the tasks carrying it. Being forgotten destroys those either way |
| The person's Microsoft Graph refresh token, encrypted at rest | 90 days after it was last used, or until the person signs out or is forgotten |

The copy is kept current on a timer only for people who signed in within the last {{IdleAfterDays}}
days. After that TodoWerk stops reading the person's tasks on its own, and nothing of theirs is
refreshed until they next sign in, which starts it again within seconds. The copy itself stays until
the person is forgotten. A hashtag manager has no reason to keep reading the tasks of somebody who is
not using it, and this is where that stops.

Task titles are the sensitive part. A title is whatever the person wrote in it, and TodoWerk holds a
copy in order to count hashtags. Every Graph token stays on the server. The browser holds a session
cookie and nothing else. The database is CloudWerk's, and the privacy notice says where it is kept.

The one line a server log may carry about a person is written when a browser refuses to keep
TodoWerk's session inside Microsoft Teams. It holds the address that failed and the browser's user
agent, and nothing that names the person. The privacy notice has the detail.

## What your people can see about each other

Any signed-in person in the organisation can open **Your organisation**, which shows counts: how
many people have signed in, how many did so inside three trailing windows, when the first of them
started, and how many hashtag occurrences they hold between them. **Counts, and nothing else.** It
never names anybody, never shows a per-person row, never lists which hashtags exist in the
organisation, and never compares one person's hashtags with another's.

While fewer than {{StatisticsFloor}} people are on record, the statistics are not shown at all. In
a tenant that small, a total plus one reader's own knowledge is an inference about identifiable
colleagues. People who have been forgotten do not count toward that {{StatisticsFloor}}. The floor is
measured on every request. A tenant that had the statistics loses them again if enough people are
forgotten, which is the floor keeping its promise.

The invitation to approve TodoWerk for the organisation is subject to no floor and appears until an
approval is recorded. Otherwise it could not be found in the tenants that most need it.

## Being forgotten, and how long things are kept

**On a person's own initiative, at any time.** *Delete my data* is in the menu under the person's
name, beside *Sign out* in the browser and beside *Open in browser* in the Teams tab. It asks for
confirmation, because it cannot be undone. It then destroys the person's indexed titles and
hashtags, their sync state, their changes and journals, their marker rules, and their stored refresh
token. Their membership record is anonymised in place. The object id and the last sign-in go, and
the tenant id and the first sign-in stay. What remains says that somebody in the organisation
started using TodoWerk on a date, and names nobody. Their tasks in Microsoft To Do are untouched.

**Automatically, after {{DormancyWindowDays}} days without a sign-in.** Everything above is
destroyed by the same mechanism with the same result. There is nothing to ask for and nobody to ask.
The same setting is the longest activity window *Your organisation* reports, so the retention rule
and what the screen claims cannot drift apart.

There is deliberately no organisation-wide delete inside TodoWerk. A button that destroys colleagues'
data on one person's authority is not something a hashtag manager should own. Revoking TodoWerk,
below, reaches the same end.

## Revoking TodoWerk

Remove TodoWerk's permissions in the Microsoft Entra admin center. They are under **Enterprise
applications**, on TodoWerk's **Permissions** page. Deleting the enterprise application outright
works too. TodoWerk is told nothing, but from that moment nobody in the organisation can sign in,
everybody goes dormant, and the {{DormancyWindowDays}}-day rule above clears the organisation's data
on its own. There is no second action to take inside TodoWerk.

If the data has to be gone sooner than that, ask CloudWerk: {{OperatorContact}}.

## Making it available

TodoWerk reaches people two ways, and both are yours to allow or block.

- **In a browser**, at the Hosted Service's address, with nothing to install. Anybody with a work or
  school account can sign in. Whether they may is decided by your organisation's consent policy and
  by the approval above.
- **As a personal tab in Microsoft Teams**, from the app catalogue in Teams. Under **Teams admin
  center → Teams apps → Manage apps** you can allow or block it and add it to a setup policy that
  installs it for people rather than leaving them to find it. The same tab appears in Outlook and in the
  Microsoft 365 app, under the same settings. The tab is personal only. There is no channel or
  group tab, because the product has nothing to show a team.

Then approve it once for the organisation, as above. It works without that. The difference is that
every person meets a consent prompt the first time they open it.

One limitation to know before rolling out: **Teams on the web in Safari** is expected not to hold a
TodoWerk session inside the Teams frame, because Safari refuses the cookie that session is. The tab
is built to detect that and offer to open TodoWerk in a browser tab instead, where everything works.
Teams desktop, Teams on Android, and Teams on the web in Edge or Chrome are unaffected.

## Licences

The Hosted Service has to know whether the person in front of it is licensed. A licence comes in
three kinds. One is bought for a whole organisation and covers everybody in it, with no cap on how
many. One is bought by a person for themselves. The third is a personal licence at no charge, valid
for one year, issued to a person automatically, once and never again, the first time they arrive
with no licence of any kind. Whether somebody is licensed is decided per person. A person with no licence meets a
closed door while their colleagues carry on. The invitation to approve TodoWerk for the organisation
is shown under an organisation-wide licence and during a trial, and never to somebody holding a
personal one. Paying for one seat is not standing to approve a product for an organisation.

To answer that question the Hosted Service sends CloudWerk's own licensing portal the organisation's
tenant id and the person's object id. It asks when the person is using TodoWerk and when their copy
is refreshed in the background, and so never about somebody idle for {{IdleAfterDays}} days or more.
Once a day, on a sign-in a human performed, it also sends a keyed hash of that object id, so that
the portal can count people without being able to name one.
None of these is a name, an address or a user principal name, and none is written to the database
as a result of being sent. The [privacy notice](../PRIVACY.md) has the table.

## Asking about this

About the Hosted Service, and about anything on this page: {{OperatorContact}}. Security
vulnerabilities go through the project's [security policy](../SECURITY.md) rather than to this
address or the issue tracker.
