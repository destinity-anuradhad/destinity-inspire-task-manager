/* ============================================================================
   seed-workstates.sql
   Run on:  DestiniyInspire_TaskManager_v2  (the live app DB)
   Purpose: Populate the WorkStates table — the SINGLE source of truth for the
            board/kanban COLUMNS and the status-change DROPDOWNS.

   Why:     WorkStates was empty, so the app fell back to a 7-status set that is
            MISSING "Development Done", "In QA" and "Blocked". Because the status
            dropdown = (item type's flow  ∩  WorkStates rows), those three
            statuses were being filtered out of every dropdown, and the Kanban /
            Sprint Board couldn't show them as columns.

   Result:  All 10 canonical statuses exist as columns (New → To Do → Active →
            In Progress → Development Done → In QA → Resolved → Done → Closed →
            Blocked). Each ITEM TYPE still only ever sees its OWN flow, driven by:
              • status dropdowns  → ALLOWED_TRANSITIONS[type]  (hardcoded, per type)
              • create-form list  → the ItemTypes column below (null = all types)

   Per-type flows (mirrors TaskStatusRules.cs — do not change without changing that):
      Task    : To Do → In Progress → Development Done → In QA → Done  (+ Blocked)
      Bug     : New   → Active → Resolved → Closed                     (+ Blocked)
      Story   : New   → Active → Resolved → Closed                     (+ Blocked)
      Feature : New   → Active → Resolved → Closed                     (+ Blocked)
      Epic    : New   → Active → Closed

   Idempotent: MERGE by Name — safe to re-run. Updates colour/order/scoping on
   existing rows, inserts missing ones, and never touches a manually-set WipLimit
   or any extra state you added by hand.
   ============================================================================ */
SET NOCOUNT ON;

DECLARE @ws TABLE (Name nvarchar(100), Color nvarchar(20), SortOrder int, ItemTypes nvarchar(200));
INSERT INTO @ws (Name, Color, SortOrder, ItemTypes) VALUES
    ('New',              '#94a3b8',  10, 'Bug,Story,Feature,Epic'),  -- start state for non-Task types
    ('To Do',            '#64748b',  20, 'Task'),                     -- Task start state
    ('Active',           '#0ea5e9',  30, 'Bug,Story,Feature,Epic'),
    ('In Progress',      '#2563eb',  40, 'Task'),
    ('Development Done', '#6366f1',  50, 'Task'),                     -- Task-only (dev complete, pending QA)
    ('In QA',            '#a855f7',  60, 'Task'),                     -- Task-only
    ('Resolved',         '#10b981',  70, 'Bug,Story,Feature'),       -- Epic has no Resolved
    ('Done',             '#16a34a',  80, 'Task'),                     -- Task terminal
    ('Closed',           '#047857',  90, 'Bug,Story,Feature,Epic'),  -- terminal for non-Task types
    ('Blocked',          '#ef4444', 100, 'Task,Bug,Story,Feature');  -- side-state; Epic has no Blocked

MERGE dbo.WorkStates AS tgt
USING @ws AS src ON tgt.Name = src.Name
WHEN MATCHED THEN UPDATE SET
    tgt.Color     = src.Color,
    tgt.SortOrder = src.SortOrder,
    tgt.ItemTypes = src.ItemTypes
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Name, Color, SortOrder, WipLimit, ItemTypes)
    VALUES (src.Name, src.Color, src.SortOrder, NULL, src.ItemTypes);

PRINT 'WorkStates seeded/updated. Current columns (left → right):';
SELECT Id, Name, Color, SortOrder, ItemTypes FROM dbo.WorkStates ORDER BY SortOrder, Name;
