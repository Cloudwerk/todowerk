namespace TodoWerk.Domain.Changes;

/// <summary>
/// One Marker Rule as a Change carries it: copied at confirmation and never read from the live
/// table again (ADR-0014).
/// <para>
/// That copy is the whole point. The runner recomputes the block from the title Graph returns, but
/// from these Markers — so a rule the user edits while an Apply is queued or running takes effect
/// at the next Apply, and the journal and undo stand without the rules table being consulted at
/// all.
/// </para>
/// <para>
/// Text rather than the <c>Marker</c> value type, because this record is serialised into a column:
/// what is stored is the emoji, and reading it back through the domain type is the runner's job,
/// not the column's.
/// </para>
/// </summary>
/// <param name="Key">The folded Hashtag key the rule is about.</param>
/// <param name="Spelling">What to call that Hashtag, so a queue entry can name it.</param>
/// <param name="Marker">The emoji, as text.</param>
/// <param name="RetiredMarker">The emoji this rule replaced and this Apply swaps out, or null.</param>
/// <param name="Position">Where the rule sits in the person's list, which is the order of the block.</param>
/// <param name="Abandoned">
/// A Marker of a rule the person has deleted. Carried so the block reader still recognises it —
/// it is at the front of every title the rule was applied to, and a reader that stopped at it
/// would put a second block in front of the first — and for nothing else: it is never wanted,
/// never in scope for an Apply, and never swapped. Absent from a Change written before deletion
/// was kept, which reads as false.
/// </param>
/// <param name="Removable">
/// This Marker is in a Remove Markers' scope: this run may take it, and the one it retired, out of
/// the blocks they sit in. Always false for an Apply, which removes nothing.
/// <para>
/// The scope of a Remove is a set of Markers rather than a set of Hashtag keys, which is why it is
/// carried here rather than in <c>SourceKeys</c> beside the other three Changes. A Remove goes
/// looking for tasks that no longer carry the Hashtag — that is what makes the Marker stale — so
/// there is no key to select them by; and the Marker of a deleted rule has no key at all. Absent
/// from a Change written before removing existed, which reads as false.
/// </para>
/// </param>
/// <param name="Reaches">
/// Every task the plan could see that carries this Marker is in the plan, so a run that reaches all
/// of them has taken it out of every block there is — which is what lets a completed Remove drop
/// the row that was keeping it known.
/// <para>
/// False when the plan knows itself to be short: a task whose title is nothing but this Marker is
/// passed over before it becomes a plan row, and a list that has never been read end to end
/// contributes nothing at all (ADR-0003). In either case the emoji is still out there afterwards,
/// and forgetting it would leave the block reader stopping in front of it — so the row stays and
/// the person is offered the removal again. Absent from a Change written before removing existed,
/// which reads as false, and false for an Apply, which drops no rows.
/// </para>
/// </param>
public sealed record AppliedMarker(
    string Key,
    string Spelling,
    string Marker,
    string? RetiredMarker,
    int Position,
    bool Abandoned = false,
    bool Removable = false,
    bool Reaches = false);
