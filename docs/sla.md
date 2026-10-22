# SLA policies

An **SLA policy** sets a target for how quickly work should be resolved: *"work of this kind should
reach a done state within N hours of being created."* A live compliance report then shows which open
work is on track, at risk, or already breaching — so teams can act before a deadline slips.

Team administrators and system administrators manage policies under **Admin → SLA** (`/Sla`).
Policies are scoped to a single team.

## Defining a policy

| Field | Meaning |
|---|---|
| **Name** | A label, e.g. "Critical bugs resolved within 8 hours". |
| **Item type** *(optional)* | Restrict the policy to one work item type (Bug, Story, …). Leave as *(any)* for all. |
| **Priority** *(optional)* | Restrict the policy to one priority (Critical, High, …). Leave as *(any)* for all. |
| **Target (hours)** | Hours from creation within which the task should be done. |

A task is governed by the **first enabled policy** (in creation order) whose scope matches it, so put
your most specific policies first.

## The compliance report

The SLA page shows a snapshot of the team's **open** work under an active policy — done work has
already met or missed its target and needs no action. Each item is one of:

| Status | Meaning |
|---|---|
| **On track** | Comfortably inside the target. |
| **At risk** | 25% or less of the target time remains. |
| **Breached** | The deadline (created + target) has already passed. |

Items are ordered most-urgent first (breaches, then soonest deadline), and summary counts sit at the
top. The deadline and time remaining (or time over) are shown per task.

## How status is computed

For an **open** task, the deadline is `CreatedAt + TargetHours`:

- **Breached** if now is past the deadline.
- **At risk** if the remaining time is ≤ 25% of the target.
- **On track** otherwise.

For a **done** task (used for historical evaluation), the status is **Met** if it was completed on or
before the deadline, otherwise **Breached**.

Evaluation is entirely read-only — it derives everything from a task's existing creation, priority,
type, status, and completion timestamp, and never changes a task.

## Relationship to automation

SLA reporting is a passive dashboard. To *act* on a deadline — e.g. remind an owner the day before a
task is due — pair it with an [automation](automation.md) `task.due_soon` rule.
