# About TodoWerk

{.lead}
TodoWerk reads the hashtags out of your Microsoft To Do tasks, shows you what you actually have, and
fixes the ones that drifted.

## What it does

### See your hashtags

For every hashtag it shows how often it is used, across how many lists, when it was last touched,
and whether it looks like a mistake: written more than one way, nearly identical to another, or not
edited in months.

### Fix them

Three changes to a hashtag, and no others:

- Rename a hashtag.
- Normalise one written several ways onto a single spelling.
- Merge several hashtags into one.

Before anything happens you see the exact task titles the change would rewrite. The change runs in
the background against Microsoft To Do and can be undone as a whole for thirty days. Only the
hashtag's own text is rewritten. The rest of the sentence stays exactly as it was.

### Mark tasks

Tell TodoWerk that a hashtag carries an emoji, say `#bread` carries 🍞, and the emoji goes at the
front of every task with that hashtag. A marker rule never writes on its own. The emoji goes on when
you apply your markers, previewed and undoable like any other change. Applying adds and reorders
markers but never takes one away, so an emoji whose hashtag you have since removed stays put until
you say otherwise. Removing markers is a separate change, previewed and undoable in the same way.
It only ever touches emoji TodoWerk put at the front of a title itself, never one you typed.
Renaming a hashtag carries its marker along.

## What it never does

- It never reaches any tasks but your own.
- It never manages anybody else's hashtags, and never counts hashtags across people.
- It never lets one person change a colleague's tasks.
- It never creates, completes or deletes a task.

## Where it runs

In a browser, and as a personal tab in Microsoft Teams, in Outlook and in the Microsoft 365 app,
with a work or school account. Consumer Microsoft accounts are not supported. TodoWerk is available
in English.

## Who runs this installation

> This installation of TodoWerk is operated by **{{Operator}}**. By signing in you agree to its
> [terms of use](../TERMS.md) and [privacy notice](../PRIVACY.md). The contact for this installation,
> and for questions about either document, is {{OperatorContact}}.

{{OperatorLinks}}

## Getting help

- **Something is not working**: you cannot sign in, or something that used to work has stopped.
  Contact {{OperatorContact}}.
- **A defect in the software, or an idea for it**: the
  [issue tracker](https://github.com/Cloudwerk/todowerk/issues). TodoWerk is developed in a public
  repository, and an issue filed there is read by the people who build it.
- **A security vulnerability**: never the issue tracker. The
  [security policy](../SECURITY.md) says where to report one privately.

## Read more

{{HandbookLinks}}
