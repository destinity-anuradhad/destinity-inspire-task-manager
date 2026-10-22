# Automation rules

Automation lets a team say **"WHEN something happens to a task (optionally, only if a condition
holds) THEN do this."** Rules run automatically as a side effect of the normal task lifecycle — the
same events that drive [webhooks](webhooks.md) — so no one has to remember to notify an owner, tag a
critical bug, or bump a priority by hand.

Team administrators and system administrators manage rules under **Admin → Automations**
(`/Automations`). Rules are scoped to a single team and take effect immediately.

## Anatomy of a rule

| Part | Meaning |
|---|---|
| **Trigger** | The lifecycle event that fires the rule. |
| **Condition** *(optional)* | A single field/operator/value test on the task. Leave the field as *(any task)* to run unconditionally. |
| **Action** | What to do when the trigger fires and the condition matches. |

### Triggers

| Trigger | Fires when |
|---|---|
| `task.created` | A task is created. |
| `task.status_changed` | A task's status changes. |
| `task.assigned` | A task's owner changes. |
| `task.comment_added` | A comment is added to a task. |
| `task.due_soon` | *(scheduled)* A task's target date is within the next day and the task isn't done. |

The first four are **event** triggers — they fire the instant the change happens. `task.due_soon` is
a **scheduled** trigger: a background sweep evaluates it about once an hour and reminds once per
task per day (so a task due tomorrow is reminded once, not every sweep). It's the way to build the
classic "remind me the day before something is due" rule.

### Conditions

A condition compares one **allow-listed** task field to a literal value — never arbitrary code.

| Field | Operators | Example |
|---|---|---|
| `Priority` | equals, is not, contains | `Priority equals Critical` |
| `Status` | equals, is not, contains | `Status equals Blocked` |
| `ItemType` | equals, is not, contains | `ItemType equals Bug` |
| `Owner` | equals, is not, contains | `Owner contains Alice` |
| *(any task)* | — | always matches |

Comparisons are case-insensitive.

### Actions

| Action | Effect | Value |
|---|---|---|
| `notify_owner` | Sends an in-app notification to each of the task's owners. | Optional message (template). |
| `notify_watchers` | Sends an in-app notification to everyone watching the task. | Optional message (template). |
| `add_comment` | Appends a system comment to the task's history. | Optional comment text (template). |
| `set_priority` | Sets the task's priority. | **Required** — the priority to apply. |
| `add_label` | Adds a label to the task (no-op if already present). | **Required** — the label to add. |

Notification and comment text may use the placeholders `{goal}`, `{status}`, `{priority}`, and
`{owner}`, which are substituted from the task at run time.

## Examples

- **Escalate critical bugs** — WHEN `task.created`, only if `ItemType equals Bug` and (a second rule)
  `Priority equals Critical`, THEN `notify_owner` "Critical bug filed: {goal}".
- **Triage inbox** — WHEN `task.created` THEN `add_label` "triage".
- **Deadline reminder** — WHEN `task.due_soon` THEN `notify_owner` "Due tomorrow: {goal}".
- **Flag blockers** — WHEN `task.status_changed`, only if `Status equals Blocked`, THEN
  `set_priority` "High".

## Testing a rule (dry run)

Before trusting a rule, use **Test a rule** on the Automations page: pick a rule and a real task ID and
click **Preview**. It reports whether the rule's condition matches that task and exactly what the
action *would* do (e.g. *"would set priority 'Low' → 'High'"*, *"would notify 2 owners"*) — without
running the action or writing anything. A great way to sanity-check a condition before enabling it.

## Safety and observability

Automation is deliberately bounded so it can never destabilise a task write:

- **Best-effort.** Rule evaluation runs after the originating command has committed. A rule failure is
  caught, audited, and never surfaces to the user or rolls back their change.
- **No cascades.** Actions are a small, fixed set that write directly and never re-enter the task
  command pipeline, so an action can't trigger another round of automation. A re-entrancy guard
  enforces this even if the action set grows.
- **Per-event cap.** At most 25 rules run for a single event, protecting every task write from a
  misconfigured team with a runaway number of rules.
- **Audit trail.** Every time a rule acts (or fails) it writes an `AutomationRuleRun` record — visible
  under **Recent activity** on the Automations page — capturing the rule, task, event, outcome, and a
  human-readable detail.

## Relationship to webhooks

Automation and [webhooks](webhooks.md) listen to the **same** event set. Webhooks push those events to
external systems; automation reacts to them **inside** the tracker. A webhook failure never skips
automation, and vice versa — the two are dispatched independently.
