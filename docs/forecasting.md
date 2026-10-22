# Delivery forecasting

A velocity-based forecast (Phase 4) that projects **how many sprints the team's remaining backlog will
take**, from its historical completed velocity. Read-only. Available to team leads and above under
**Forecast** (`/Forecast`).

## Inputs (both authoritative, reused from existing features)

- **Velocity baseline** — the immutable `SprintCompletionSnapshot.CompletedEstimate` of the team's
  completed sprints (the same historical facts the completed-sprint burndown uses). By default the most
  recent **6** completed sprints form the baseline.
- **Remaining backlog** — the sum of `Weight` for the team's **open** (not-done) **point-estimated**
  tasks. "Done" uses the item-type-aware `TaskStatusRules.IsDone`; hour-estimated work is excluded
  (velocity is measured in points).

## Output

| Figure | Meaning |
|---|---|
| Average velocity | Mean completed points per sprint over the baseline window |
| Remaining points | Open point-estimated backlog |
| **Expected** | `ceil(remaining / average velocity)` sprints |
| **Optimistic** | `ceil(remaining / max velocity)` — fewest sprints |
| **Pessimistic** | `ceil(remaining / min velocity)` — most sprints |

The range (optimistic → expected → pessimistic) communicates uncertainty honestly rather than a single
false-precision number. A velocity history table shows the per-sprint completed points behind the
average.

## Guards & honesty

- **Not enough data:** with no completed sprint (or zero average velocity) the forecast reports
  *"not enough history"* and still shows the remaining backlog — it never invents a projection.
- **Nothing remaining:** projects **0 sprints** (done).
- **Team-scoped:** velocity and backlog are both filtered to the team; one team's numbers never leak
  into another's forecast.

## Notes / extension points

- The baseline window (default 6 sprints) is a parameter on `IForecastService.GetForecastAsync`; expose
  it in the UI if teams want to tune how much history counts.
- This is a point-in-time projection, not a Monte-Carlo simulation. A future enhancement could add a
  confidence band from the velocity distribution (e.g. 85th-percentile forecasting) — the per-sprint
  history is already available to drive it.
