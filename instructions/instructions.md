# EXTENDED MASTER REQUIREMENTS — ENTERPRISE TASK / WORK MANAGEMENT PLATFORM

The previous testing instructions remain fully applicable.

However, this application must NOT be evaluated only against the features that currently exist in the codebase.

The target product quality is an **enterprise-grade Task / Work / Project Management platform comparable in capability, reliability, workflow maturity, usability, and administration quality to platforms such as:**

- Azure DevOps Boards
- Jira Software
- ClickUp

Do NOT attempt to blindly clone these products.

Instead, use them as maturity benchmarks.

Your responsibility is to determine:

1. What already exists.
2. What works correctly.
3. What exists but is incomplete.
4. What exists but is incorrectly designed.
5. What important enterprise workflow is missing.
6. What should be added.
7. What should NOT be added because it does not fit this product.
8. How the system can evolve into a production-grade enterprise work-management product.

---

# 1. COMPLETE PRODUCT DISCOVERY

Do not depend only on documentation.

Read the full codebase and discover ALL functionality.

Inspect:

- Routes
- Navigation menus
- Components
- Controllers
- Services
- Repositories
- Models
- DTOs
- Database tables
- Stored procedures
- Background services
- Scheduled jobs
- Email templates
- Notification handlers
- Permissions
- Roles
- Feature flags
- Settings
- Configuration
- Reporting
- Dashboards
- Audit logs
- Integrations

Build a complete system capability inventory.

If functionality exists in code but is hidden from UI, record it.

If UI exists without working backend logic, record it.

If database structures suggest unfinished functionality, investigate it.

---

# 2. ENTERPRISE FEATURE GAP ANALYSIS

After understanding the existing product, perform a complete gap analysis against a mature enterprise work management platform.

For every capability classify it as:

- Fully Implemented
- Partially Implemented
- Implemented but Defective
- Implemented but Poor UX
- Backend Only
- UI Only
- Missing
- Not Applicable
- Future Enhancement

Do not classify a feature as missing simply because Jira, Azure DevOps, or ClickUp contains it.

Explain whether it is genuinely valuable to THIS system.

---

# 3. COMPLETE WORK ITEM MODEL

Investigate whether the product supports an appropriate hierarchy.

Possible work-item types may include:

- Initiative
- Portfolio Item
- Project
- Epic
- Feature
- User Story
- Requirement
- Task
- Sub-task
- Bug
- Issue
- Improvement
- Support Ticket
- Spike
- Change Request

Use the actual terminology in this application.

Determine whether administrators can configure work-item types.

Check whether work-item relationships are flexible enough.

---

# 4. TASK LIFECYCLE MUST BE TESTED END-TO-END

Test the COMPLETE Task lifecycle.

Example:

Create Task

→ Assign Task

→ Add description

→ Add estimate

→ Set priority

→ Set dates

→ Add labels

→ Add comments

→ Add attachments

→ Add dependencies

→ Add subtasks

→ Add to backlog

→ Add to Sprint

→ Start work

→ Update progress

→ Change assignee

→ Review

→ Testing

→ Complete

→ Close

→ Reopen

→ Archive

Actual workflow must be derived from the code.

Test every supported state transition.

---

# 5. SUB-TASK BUSINESS LOGIC — DEEP TESTING

Sub-task functionality must receive special testing.

Investigate:

- Create sub-task
- Multiple sub-tasks
- Nested sub-tasks if supported
- Assign sub-task separately
- Different sub-task owners
- Different priorities
- Different estimates
- Different due dates
- Sub-task comments
- Sub-task attachments
- Sub-task dependencies
- Sub-task status
- Sub-task notifications
- Sub-task audit history

---

# 6. MAIN TASK AUTO-COMPLETION FROM SUB-TASKS

Test the relationship between parent Task and sub-tasks.

Example business rule:

Main Task

- Sub-task A — Completed
- Sub-task B — Completed
- Sub-task C — Completed

When ALL required sub-tasks are completed, investigate whether the Main Task should automatically become Completed.

Determine the intended business rule from the application.

Test:

### Scenario A

3 subtasks.

Complete 1.

Main Task must NOT incorrectly complete.

### Scenario B

Complete 2 of 3.

Main Task must remain incomplete.

### Scenario C

Complete all 3.

Determine expected parent behavior.

Possible valid behaviors:

- Automatically complete parent.
- Prompt user to complete parent.
- Update parent progress to 100% but require manual completion.

Determine which approach best fits the product.

If the system currently has no defined behavior, report it as a **Business Workflow Gap** and recommend the most appropriate enterprise behavior.

---

# 7. REOPENING SUB-TASK AFTER PARENT COMPLETION

Test:

1. Complete all sub-tasks.
2. Complete parent Task.
3. Reopen one sub-task.

Determine what happens to parent Task.

Possible valid options:

- Automatically reopen parent.
- Mark parent as partially complete.
- Warn user.
- Prevent sub-task reopen until parent is reopened.

The behavior must be consistent and documented.

---

# 8. PARENT / CHILD PROGRESS CALCULATION

Investigate Task progress calculation.

Example:

4 subtasks.

3 completed.

Expected progress could be:

75%.

But weighted estimation may also exist.

Example:

Subtask A = 1 hour

Subtask B = 1 hour

Subtask C = 8 hours

Progress should potentially be estimate-weighted rather than item-count based.

Investigate existing logic.

Test and recommend enterprise-grade behavior.

---

# 9. TASK SPLITTING

The application should be reviewed for Task Splitting functionality.

A user may need to divide one large Task into multiple work items.

Example:

Original Task:

"Build Payment Module"

Split into:

- Payment API
- Payment UI
- Payment Validation
- Payment Automation Tests

Determine whether the system supports this.

---

# 10. SPRINT-WISE TASK SPLITTING

Test whether one Task can be split across Sprints.

Example:

Original Task effort:

16 hours.

Sprint 10:

8 hours.

Sprint 11:

8 hours.

Enterprise systems often manage this by:

- Splitting Task into separate child tasks.
- Creating remaining-work items.
- Carrying incomplete work forward.
- Moving unfinished Task to next Sprint.

Determine the best model for this system.

Investigate whether direct partial Sprint allocation creates reporting problems.

Report:

- Current behavior
- Problems
- Recommended design
- Data model changes if required
- UI changes
- API changes
- Sprint reporting impact

---

# 11. INCOMPLETE TASK CARRY-FORWARD

When Sprint completes:

If Task is incomplete:

Test all supported behaviors.

Possible options:

- Move to backlog
- Move to next Sprint
- Split completed/remaining work
- Automatically create continuation Task
- Keep original Task history

Ensure previous Sprint reporting remains historically accurate.

A Task moved from Sprint 4 to Sprint 5 must not rewrite Sprint 4 history incorrectly.

---

# 12. SPRINT HISTORY MUST BE IMMUTABLE

After Sprint completion, investigate whether:

- Completed Sprint scope can be changed.
- Completed Tasks can be removed.
- Estimates can be edited.
- Sprint dates can be modified.
- Historical velocity changes unexpectedly.

Enterprise reporting must preserve historical Sprint information.

Report problems that can corrupt historical reporting.

---

# 13. SPRINT PLANNING

Evaluate:

- Sprint Goal
- Sprint capacity
- Team availability
- Story points
- Hour estimates
- Workload
- User capacity
- Holidays
- Leave
- Existing workload
- Backlog priority

Determine whether Sprint over-allocation can occur.

---

# 14. USER CAPACITY MANAGEMENT

Investigate whether each user can have Sprint capacity.

Example:

Developer A:

40 available hours.

Existing work:

36 hours.

New assigned Task:

10 hours.

System should at minimum make over-allocation visible.

Possible capabilities:

- Capacity warning
- Workload dashboard
- Utilization report

Recommend appropriate functionality.

---

# 15. WORKLOAD MANAGEMENT

Evaluate enterprise workload visibility.

User should potentially be able to see:

- Assigned work
- Current Sprint work
- Overdue Tasks
- Upcoming Tasks
- Estimated hours
- Actual hours
- Remaining work
- Blocked work
- Completed work

Managers may need workload view across users.

---

# 16. USER MANAGEMENT — COMPLETE AUDIT

Deeply inspect User Management.

Test:

- Create user
- Invite user
- Activate user
- Deactivate user
- Suspend user
- Delete user
- Restore user
- Edit profile
- Change team
- Change role
- Change project access
- Reset password if applicable
- Last login
- Account status

Check what happens to existing Tasks when a user is deactivated.

Tasks must not become orphaned.

---

# 17. TEAM MANAGEMENT

Determine whether Teams exist.

Test:

- Create team
- Rename team
- Add member
- Remove member
- Team lead
- Team projects
- Team Sprint
- Team workload
- Team permissions

---

# 18. ROLE-BASED ACCESS CONTROL

Perform a comprehensive RBAC audit.

Discover REAL roles from code/database.

Examples may include:

- System Administrator
- Organization Administrator
- Project Administrator
- Project Manager
- Team Lead
- Developer
- QA
- Member
- Viewer
- Guest

Do not assume these roles exist.

---

# 19. PERMISSION GRANULARITY

Check permissions for actions including:

- View project
- Create project
- Edit project
- Delete project
- Archive project
- Manage members
- Create Epic
- Edit Epic
- Delete Epic
- Create Sprint
- Start Sprint
- Complete Sprint
- Create Task
- Edit own Task
- Edit other user's Task
- Assign Task
- Change status
- Delete Task
- Close Task
- Reopen Task
- Add comments
- Delete comments
- Upload attachment
- View reports
- Export reports
- Manage automation
- Manage settings

---

# 20. PROJECT-LEVEL ACCESS CONTROL

Test whether a user who belongs to:

Project A

can access:

Project B

through:

- Navigation
- Direct URL
- API
- Search
- Dashboard
- Reports
- Notifications
- File attachments

This is a critical security area.

---

# 21. RECORD-LEVEL ACCESS CONTROL

Check whether users can access unauthorized individual Tasks by changing IDs.

Example:

/tasks/100

→

/tasks/101

This must be tested safely for IDOR-style authorization issues.

---

# 22. EMAIL AUTOMATION — REQUIRED FULL TEST

Email automation must be considered a core system feature.

Inspect all code related to:

- SMTP
- Email provider
- Templates
- Background jobs
- Queues
- Scheduled jobs
- Trigger handlers
- Retry logic
- Failure handling
- Email logs

---

# 23. EMAIL EVENT MATRIX

Identify all business events that should potentially trigger email.

For example:

- User invited
- Account activated
- Task created
- Task assigned
- Task reassigned
- Task due soon
- Task overdue
- Task completed
- Task reopened
- Comment added
- User mentioned
- Attachment added
- Bug assigned
- Bug resolved
- Sprint started
- Sprint ending soon
- Sprint completed
- Epic completed
- Deadline changed
- Project member added
- Project member removed

Determine which events the current application supports.

---

# 24. EMAIL AUTOMATION FUNCTIONAL TESTING

For each supported email trigger:

1. Perform triggering UI action.
2. Confirm backend event.
3. Confirm email job creation.
4. Confirm email service execution.
5. Confirm recipient.
6. Confirm subject.
7. Confirm template.
8. Confirm dynamic values.
9. Confirm URL.
10. Confirm email was sent.
11. Confirm duplicate email was NOT sent.

---

# 25. EMAIL CONTENT VALIDATION

Check generated emails for:

- Recipient name
- Task title
- Project
- Assignee
- Due date
- Status
- Comment
- Action link
- Company branding
- Footer

Check broken placeholders such as:

{{TaskName}}

appearing in final emails.

---

# 26. EMAIL LINK TESTING

Links inside emails must be tested.

Example:

"View Task"

should open the correct Task.

After authentication:

The user should reach the intended Task rather than dashboard/home page.

Check authorization.

---

# 27. EMAIL AUTOMATION NEGATIVE TESTS

Test:

- Invalid email address
- Deactivated user
- Deleted user
- Missing email
- SMTP unavailable
- Timeout
- Provider error

Application business transaction should not incorrectly fail because notification delivery failed, unless explicitly designed that way.

---

# 28. EMAIL RETRY & FAILURE MANAGEMENT

Determine whether failed email delivery supports:

- Retry count
- Retry delay
- Dead-letter state
- Failure log
- Admin visibility

Repeated failure must not create infinite retries.

---

# 29. DUPLICATE EMAIL PREVENTION

Test rapid repeated updates.

One Task assignment should not produce unintended duplicate notifications.

Check idempotency.

---

# 30. USER NOTIFICATION PREFERENCES

Determine whether users can choose:

- Assignment emails
- Mention emails
- Comment emails
- Daily digest
- Due-date reminders
- Sprint reminders

Recommend notification preferences if absent and valuable.

---

# 31. IN-APP NOTIFICATIONS

Investigate:

- Notification bell
- Unread count
- Read/unread
- Mark all read
- Deep links
- Notification history
- Duplicate notifications

Check email and in-app notification consistency.

---

# 32. MENTIONS

Test:

@username

inside:

- Task comments
- Description
- Bug comments

Expected behavior may include:

- Notification
- Email
- Deep link
- Permission check

---

# 33. WATCHERS / FOLLOWERS

Evaluate whether users can follow a Task without being assignee.

Potential events:

- Status change
- Comment
- Attachment
- Assignment
- Due date

Determine whether this feature is useful for the system.

---

# 34. RECURRING TASKS

Investigate recurring Task capabilities.

Examples:

Daily

Weekly

Monthly

Custom recurrence.

Test:

- Start date
- End date
- Next generation
- Assignment
- Weekend handling
- Recurrence modification
- Cancellation

If absent, determine whether it belongs in the product roadmap.

---

# 35. TASK TEMPLATES

Evaluate whether frequently created Tasks can use templates.

Templates may contain:

- Description
- Subtasks
- Checklist
- Priority
- Labels
- Assignee
- Estimate

Recommend only if valuable.

---

# 36. CHECKLISTS

Determine whether Tasks need lightweight checklists separate from subtasks.

Example:

Deployment Task:

- Backup database
- Publish API
- Publish UI
- Run smoke test

Check whether full Sub-tasks are unnecessary for simple checklist items.

---

# 37. DEPENDENCY MANAGEMENT

Deep test relationships:

- Blocks
- Blocked by
- Depends on
- Related to
- Duplicates
- Parent of
- Child of

If Task A is blocked:

The UI should make that clear.

Potentially prevent completion where business rules require.

---

# 38. CIRCULAR DEPENDENCY

Test:

A depends on B.

B depends on C.

Attempt:

C depends on A.

System should detect circular relationships if dependencies influence workflow.

---

# 39. CRITICAL PATH / DEPENDENCY VISIBILITY

Evaluate whether project managers can understand which Task delays affect project completion.

If this is outside current scope, list it as a future enhancement rather than mandatory bug.

---

# 40. TIME TRACKING

Investigate whether users can:

- Log work
- Start timer
- Stop timer
- Enter manual time
- Edit time
- Delete time

Track:

- Original estimate
- Logged time
- Remaining estimate

---

# 41. TIME TRACKING RULES

Test:

Estimated = 8 hours.

Logged = 5 hours.

Remaining = 3 hours.

If user logs another 5 hours:

Determine expected behavior.

Should system show:

10h logged / 8h estimated

instead of corrupting the estimate.

---

# 42. AUDIT HISTORY

Every important enterprise action should have appropriate traceability.

Examples:

Task created

Task edited

Assignee changed

Priority changed

Sprint changed

Estimate changed

Due date changed

Status changed

Task deleted

Task restored

User permission changed

---

# 43. AUDIT IMMUTABILITY

Users should not normally be able to modify historical audit entries.

Test whether history can be manipulated.

---

# 44. SOFT DELETE / RESTORE

Review deletion strategy.

Enterprise systems often require soft-delete for important records.

Investigate:

- Projects
- Tasks
- Bugs
- Users
- Comments
- Attachments

Determine where restore capability is appropriate.

---

# 45. BULK OPERATIONS

Evaluate:

- Bulk assign
- Bulk status update
- Bulk Sprint move
- Bulk priority
- Bulk delete
- Bulk archive
- Bulk labels

Check authorization and partial failures.

---

# 46. DRAG AND DROP

Test board/backlog drag/drop thoroughly.

Verify:

UI update

→ API

→ DB

→ Reload

→ Other user session

all remain consistent.

---

# 47. REAL-TIME COLLABORATION

Determine whether data updates require browser refresh.

Example:

User A changes Task.

User B has same board open.

Does User B see update?

Possible implementations:

- SignalR
- WebSockets
- Polling
- Manual refresh

Assess whether real-time updates are required for product maturity.

---

# 48. CONFLICT HANDLING

If User A and User B edit same Task simultaneously:

Determine:

- Last write wins?
- Optimistic concurrency?
- Version check?
- Warning?

Report potential lost-update risks.

---

# 49. CUSTOM FIELDS

Evaluate whether projects need customizable fields.

Examples:

Customer

Environment

Module

Release

Severity

Business Unit

If custom fields exist:

Test type validation, filtering, reporting and permissions.

---

# 50. LABEL / TAG MANAGEMENT

Test:

- Create
- Rename
- Delete
- Color if available
- Filter
- Duplicate labels
- Cross-project behavior

---

# 51. PRIORITY MANAGEMENT

Determine priority model.

Example:

Critical

Highest

High

Medium

Low

Lowest.

Ensure sort/filter behavior works.

---

# 52. BUG SEVERITY VS PRIORITY

Verify distinction:

Severity = technical/business impact.

Priority = order in which work should be addressed.

These should not be incorrectly treated as identical unless intentionally designed.

---

# 53. RELEASE / VERSION MANAGEMENT

Investigate:

- Release
- Version
- Milestone
- Fix Version

Potential workflow:

Bug

→ Fix Version 2.3

→ Release 2.3

→ Deployed.

Determine whether current product needs this capability.

---

# 54. DASHBOARD

Audit dashboard usefulness.

Potential widgets:

- My Tasks
- Overdue Tasks
- Tasks Due Today
- Active Sprint
- Sprint progress
- Project health
- Bugs by severity
- Workload
- Recently updated
- Blocked Tasks

Check accuracy of every displayed count.

---

# 55. EXECUTIVE / MANAGEMENT REPORTING

Evaluate reports such as:

- Sprint velocity
- Burndown
- Burnup
- Cumulative flow
- Workload
- Task completion trend
- Bug trend
- Aging issues
- Overdue Tasks
- Cycle time
- Lead time
- User productivity
- Project progress

Do not recommend misleading vanity metrics.

---

# 56. BURNDOWN ACCURACY

If burndown exists:

Verify historical data rather than calculating only from current state.

Sprint history must remain accurate after Task changes.

---

# 57. KANBAN METRICS

Where relevant evaluate:

- Work In Progress
- Cycle time
- Lead time
- Throughput

Check whether WIP limits exist or would be valuable.

---

# 58. SLA / DUE DATE MANAGEMENT

For Task/Bug workflows, determine whether due dates need:

- Reminder
- Escalation
- Overdue status
- SLA breach

Especially valuable for support/issue-tracking use cases.

---

# 59. AUTOMATION RULE ENGINE

Evaluate whether system supports or would benefit from automation rules.

Examples:

WHEN Task assigned

THEN Send Email.

WHEN all Subtasks Completed

THEN Complete Parent Task.

WHEN Due Date is tomorrow

THEN Notify Assignee.

WHEN Bug severity = Critical

THEN Notify Project Manager.

WHEN Task moved to Done

THEN Set Completed Date.

---

# 60. AUTOMATION RULE SAFETY

Check:

- Infinite loops
- Duplicate execution
- Cyclic rules
- Permission context
- Failed rule handling
- Audit logs

---

# 61. BUSINESS AUTOMATION TESTS

Explicitly test important automations such as:

### Automation A

All Subtasks Completed

→ Parent completion/progress behavior.

### Automation B

Task Assigned

→ In-app notification

→ Email.

### Automation C

Task Due Soon

→ Reminder.

### Automation D

Task Overdue

→ Notification/escalation.

### Automation E

Bug Resolved

→ Reporter/QA notification.

### Automation F

Sprint nearing end

→ Team reminder.

Only mark an automation as PASS after verifying actual execution.

---

# 62. APPROVAL / REVIEW WORKFLOW

Investigate whether completed work requires review.

Example:

Developer:

In Progress

→ Code Review

QA:

Ready for QA

→ Testing

→ Passed

→ Done.

Determine whether configurable workflow is appropriate.

---

# 63. QA WORKFLOW

For Bug/Task verification:

Resolved

must not necessarily mean

Closed.

Possible lifecycle:

Open

→ Assigned

→ In Progress

→ Resolved

→ Ready for QA

→ Verified

→ Closed.

If QA fails:

→ Reopened.

Evaluate current implementation.

---

# 64. WORKFLOW CONFIGURATION

Enterprise maturity may require configurable status workflows per Project.

Determine whether:

- Statuses are hardcoded.
- Workflow is global.
- Workflow can vary by Project.
- Transition permissions exist.

Report limitations.

---

# 65. COMMENTS & DISCUSSION

Test enterprise collaboration quality.

Include:

- Comments
- Replies
- Mentions
- Edit
- Delete
- Timestamps
- Author
- Rich text
- Links
- Attachments

---

# 66. ACTIVITY FEED

Determine whether users can understand what recently changed.

Project activity may include:

- Created Tasks
- Status changes
- Comments
- Assignments
- Sprint events
- Bug resolutions

---

# 67. FILE ATTACHMENTS

Test file workflows completely:

Upload

→ Preview

→ Download

→ Permission

→ Delete.

Check attachment access through direct URL.

---

# 68. IMPORT

Evaluate whether enterprise onboarding requires:

- CSV import
- Excel import
- Jira import
- Azure DevOps import

At minimum provide roadmap recommendation if valuable.

---

# 69. EXPORT

Evaluate:

- CSV export
- Excel export
- PDF reports

Permissions and filters must be respected.

Sensitive data must not leak through export.

---

# 70. ADVANCED SEARCH

Determine whether enterprise search supports:

- Task ID
- Title
- Description
- Assignee
- Project
- Sprint
- Epic
- Status
- Priority
- Label
- Date

Advanced filtering should be assessed.

---

# 71. SAVED FILTERS / VIEWS

Determine whether users can save useful views such as:

"My Open Bugs"

"High Priority Sprint Tasks"

"Overdue Tasks"

Evaluate as feature gap if valuable.

---

# 72. FAVORITES / PINNING

Consider ability to pin:

- Projects
- Boards
- Saved views

Only recommend if valuable.

---

# 73. PERSONAL USER EXPERIENCE

Audit user-specific pages.

Potentially:

- My Tasks
- Assigned to Me
- Created by Me
- Watching
- Recently Viewed
- Notifications
- Profile
- Preferences

---

# 74. ACCESSIBILITY

Perform basic accessibility checks:

- Keyboard navigation
- Form labels
- Focus
- Dialogs
- Contrast
- Accessible names
- Error messages

Report major usability barriers.

---

# 75. MULTI-TENANCY

If application supports organizations/companies/tenants:

This is CRITICAL.

Test isolation between Tenant A and Tenant B.

Check:

- API
- Search
- IDs
- Reports
- Attachments
- Notifications
- Database queries
- Caches

Cross-tenant data leakage is CRITICAL/P0.

---

# 76. ORGANIZATION ADMINISTRATION

If multi-tenant, evaluate:

- Organization
- Workspace
- Members
- Invitations
- Roles
- Project creation
- Organization settings
- Billing hooks if present
- Data isolation

---

# 77. GLOBAL ADMINISTRATION

Investigate system administration:

- User management
- Tenant management
- Feature flags
- Email configuration
- Logs
- Background jobs
- Failed jobs
- System health
- Global settings

---

# 78. BACKGROUND JOB REVIEW

Inspect schedulers/queues.

Possible jobs:

- Emails
- Reminders
- Overdue processing
- Recurring Tasks
- Reports
- Cleanup
- Notification digest

Check:

- Retry
- Idempotency
- Scheduling
- Failure visibility
- Duplicate execution

---

# 79. APPLICATION RESTART SAFETY

Pending jobs should not be silently lost when application restarts.

Investigate persistence where relevant.

---

# 80. SYSTEM HEALTH

Assess observability:

- API health check
- Database connectivity
- Email provider status
- Background job status
- Queue health
- Application logs

Recommend improvements for production operations.

---

# 81. SECURITY AUDIT EXPANSION

Review:

- Authentication
- Authorization
- Tenant isolation
- Token handling
- Session handling
- Cookie settings
- CSRF
- CORS
- XSS
- SQL injection
- File upload security
- Secrets
- Logs
- Error handling
- Rate limiting
- Password policy
- Account lockout where applicable

Remain non-destructive.

---

# 82. PERFORMANCE & SCALE

Think beyond one user.

Evaluate behavior conceptually and practically for:

- 10 users
- 100 users
- 1,000 users
- Large projects
- Thousands of Tasks
- Large Sprint backlog
- Large activity history
- Large notification volume

Identify obvious scalability bottlenecks.

---

# 83. DATABASE SCALE

Review indexes for frequently queried fields such as:

- ProjectId
- SprintId
- AssigneeId
- StatusId
- EpicId
- TenantId
- DueDate
- CreatedDate

Do not recommend indexes blindly.

Use query patterns.

---

# 84. PAGINATION MUST BE SERVER-SIDE WHERE APPROPRIATE

Identify screens loading entire datasets into browser unnecessarily.

Report performance risks.

---

# 85. API CONSISTENCY

Review:

- Response envelope
- Error model
- Status codes
- Pagination
- Filtering
- Sorting
- Validation responses

Inconsistent APIs should be documented.

---

# 86. PRODUCTION RECOVERY

Evaluate:

- Transaction integrity
- Error rollback
- Partial writes
- Background job failures
- Retry safety

Example:

Task creation succeeds.

Email fails.

Task should usually remain successfully created.

---

# 87. FEATURE COMPARISON MATRIX

Create this additional report:

| Capability | Current System | Jira-Level Expectation | Azure DevOps-Level Expectation | ClickUp-Level Expectation | Recommended for This Product | Gap |
|---|---|---|---|---|---|---|

Do not copy competitors blindly.

Use the comparison only to assess maturity.

---

# 88. PRODUCT MATURITY MATRIX

Score:

- Work Management
- Project Management
- Agile/Sprint Management
- Collaboration
- User Management
- Access Control
- Notifications
- Email Automation
- Workflow Automation
- Reporting
- Search
- Auditability
- Integration Readiness
- Administration
- Security
- Scalability
- UX

Score each:

0–5

Where:

0 = Missing

1 = Prototype

2 = Basic

3 = Functional

4 = Production Grade

5 = Enterprise Grade

Explain each score.

---

# 89. IDENTIFY MISSING CORE FEATURES

Create a dedicated section:

# Missing Core Features

For each missing capability include:

## Feature

## Why It Matters

## User / Business Problem

## Priority

Must Have / Should Have / Could Have

## Recommended Workflow

## Suggested UI

## Suggested Backend Changes

## Suggested Database Changes

## Permissions Required

## Email/Notification Impact

## Automation Impact

## Testing Requirements

## Estimated Complexity

S / M / L / XL

---

# 90. IDENTIFY INCOMPLETE FEATURES

Separate missing features from incomplete ones.

Example:

Task comments exist but:

- Mentions don't work.
- Notifications missing.
- Permission checking missing.

This is an incomplete feature, not three unrelated bugs.

---

# 91. BUSINESS PROCESS MAPS

Produce workflow maps for major areas.

Examples:

## Task Flow

Backlog

→ Selected

→ In Progress

→ Review

→ QA

→ Done

→ Closed

## Bug Flow

Reported

→ Triaged

→ Assigned

→ Fixing

→ Resolved

→ QA Verification

→ Closed

→ Reopened if failed

## Sprint Flow

Planning

→ Ready

→ Active

→ Completing

→ Completed

Use real system behavior.

---

# 92. USER JOURNEY TESTING

Test complete journeys rather than isolated screens.

### Project Manager Journey

Create Project

→ Add Team

→ Create Epic

→ Create Sprint

→ Add Tasks

→ Estimate

→ Assign

→ Start Sprint

→ Monitor progress

→ Resolve blockers

→ Complete Sprint

→ Review report.

### Developer Journey

Login

→ View My Tasks

→ Open Task

→ Add comment

→ Update status

→ Log work

→ Complete subtasks

→ Complete Task.

### QA Journey

View assigned Bugs

→ Reproduce

→ Update Bug

→ Verify fix

→ Close/Reopen.

### Admin Journey

Create user

→ Assign role

→ Add to Project

→ Modify permissions

→ Deactivate user

→ Audit changes.

Use actual supported user types.

---

# 93. EMAIL AUTOMATION USER JOURNEY

Example:

Project Manager creates Task

→ Assigns Developer

→ Developer receives notification/email

→ Developer clicks email link

→ Logs in if necessary

→ Opens exact Task

→ Completes Task

→ Manager receives completion notification.

Test this end-to-end.

---

# 94. FULL PLAYWRIGHT AUTOMATION COVERAGE PLAN

Create recommended E2E suites:

auth.spec

users.spec

roles.spec

projects.spec

epics.spec

sprints.spec

tasks.spec

subtasks.spec

bugs.spec

board.spec

backlog.spec

notifications.spec

email-trigger.spec

permissions.spec

reports.spec

automation.spec

regression.spec

Do not necessarily implement everything unless requested.

Recommend priority.

---

# 95. BUG VS FEATURE GAP VS DESIGN ISSUE

Every finding must clearly identify whether it is:

### BUG

Existing expected behavior fails.

### FEATURE GAP

Important capability does not exist.

### INCOMPLETE FEATURE

Capability exists but significant workflow pieces are missing.

### DESIGN ISSUE

Implementation works but architecture/business design is problematic.

### UX ISSUE

Functional but unnecessarily difficult/confusing.

Do NOT report every missing enterprise feature as a Bug.

---

# 96. REQUIREMENT PRIORITIZATION

Use:

## MUST HAVE

Required before serious enterprise production usage.

## SHOULD HAVE

Important for product maturity.

## COULD HAVE

Useful enhancement.

## NOT REQUIRED

Competitor functionality that would unnecessarily complicate this product.

---

# 97. ENTERPRISE ROADMAP

After all testing create:

# Enterprise Product Roadmap

## Phase 1 — Production Stabilization

Bugs

Security

Data integrity

Authorization.

## Phase 2 — Core Work Management

Tasks

Subtasks

Epics

Sprints

Backlog

Workflows.

## Phase 3 — Collaboration

Comments

Mentions

Notifications

Email.

## Phase 4 — Automation

Rules

Reminders

Recurring Tasks.

## Phase 5 — Management Intelligence

Dashboards

Reports

Capacity

Velocity.

## Phase 6 — Enterprise Administration

Fine-grained permissions

Audit

Organization controls.

## Phase 7 — Scale & Integrations

Import/export

APIs

External integrations

Performance.

Only include phases relevant to findings.

---

# 98. DEVELOPMENT BACKLOG

Generate a development-ready backlog.

Every item must have:

ID

Type

Title

Description

Business Value

Acceptance Criteria

Priority

Severity if Bug

Dependencies

Affected Modules

Estimated Complexity

Suggested Sprint

Testing Requirement

---

# 99. ACCEPTANCE CRITERIA

For feature recommendations create clear acceptance criteria.

Example:

## Feature: Automatic Parent Completion

Acceptance Criteria:

- Given a Task has three required Subtasks,
- When all three Subtasks reach Done,
- Then parent Task progress becomes 100%.
- Parent Task status follows configured project workflow.
- Parent status change is recorded in activity history.
- Parent assignee receives notification where configured.
- Automation runs once.
- Reopening a Subtask triggers configured parent behavior.
- API and UI states remain consistent.

---

# 100. FINAL REPORT MUST ANSWER PRODUCT-LEVEL QUESTIONS

The final report must clearly answer:

1. Is the current system functional?

2. Is it production-ready?

3. Is it enterprise-ready?

4. What critical bugs exist?

5. What major workflows are incomplete?

6. What important functionality is missing?

7. Are Tasks/Subtasks designed correctly?

8. Is Sprint handling correct?

9. Is incomplete-work carry-forward safe?

10. Is Task splitting properly designed?

11. Does parent/subtask automation work correctly?

12. Does email automation work reliably?

13. Are user permissions secure?

14. Is project/user isolation correct?

15. Are notifications reliable?

16. Can managers understand workload?

17. Can administrators manage the platform safely?

18. Does historical Sprint/report data remain accurate?

19. What could cause data loss/corruption?

20. What can become a scalability problem?

21. What should be fixed first?

22. What features must be built next?

23. How far is this product from Jira/Azure DevOps/ClickUp-level maturity?

24. What realistic roadmap can move it toward that standard?

---

# 101. FINAL PRODUCT VERDICT

Give TWO separate verdicts.

## Production Readiness

One of:

READY

READY WITH MINOR FIXES

NOT READY — MAJOR FIXES REQUIRED

NOT READY — CRITICAL RISK

## Enterprise Product Maturity

One of:

PROTOTYPE

EARLY PRODUCT

FUNCTIONAL PRODUCT

PRODUCTION-GRADE PRODUCT

ENTERPRISE-READY PRODUCT

Explain both separately.

A system can be production-ready for a small team while still not being enterprise-ready.

---

# 102. MOST IMPORTANT RULE

Do NOT only tell me what is wrong.

I want you to tell me:

- What exists.
- What works.
- What does not work.
- What is incomplete.
- What is missing.
- Why it matters.
- How competitors/mature products solve the problem conceptually.
- What solution best fits THIS application.
- What developers need to change.
- What database/API/UI changes are required.
- How QA should verify the change.
- Which Sprint should contain the work.
- What should be prioritized before production.

The output must be usable as a **real product engineering roadmap**, not merely a QA bug list.

---

# 103. DO NOT STOP AT CURRENT REQUIREMENTS

The source code may reveal functionality that I did not explicitly mention.

Investigate it.

For example, if you discover:

- user permissions
- teams
- labels
- releases
- notifications
- automation rules
- reports
- dashboards
- attachments
- work logs
- custom fields
- integrations

include those modules in the audit.

Likewise, if an essential enterprise workflow is missing, identify and recommend it.

---

# 104. FIRST ACTION

Start by reading the ENTIRE repository and producing an internal map of:

1. Architecture
2. Modules
3. Screens
4. APIs
5. Database entities
6. Workflows
7. Roles
8. Permissions
9. Background jobs
10. Email/notification mechanisms

Then run the application.

Use Playwright for the real UI.

When authentication is required, ask me for test credentials.

After authentication, continue testing all accessible roles and workflows.

Do not modify production code until the investigation/reporting phase has established the problems.

The final goal is to provide a **deep engineering + QA + product gap analysis capable of guiding this application toward a Jira / Azure DevOps / ClickUp-class enterprise work-management platform.**