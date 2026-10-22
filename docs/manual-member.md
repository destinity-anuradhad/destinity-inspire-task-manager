# Member Manual
## Team Destinity Inspire — Task Tracker

---

## 1. Your Role

As a **Member**, you work on tasks assigned to you and your team. You can create tasks, update your own work, and collaborate with your teammates. You do not manage users, sprints, or team settings.

---

## 2. Logging In

1. Open the app in your browser.
2. Enter your **username** and **password**.
3. Complete the reCAPTCHA check.
4. Click **Login**.

If you belong to only one team, you are taken directly to the dashboard. If you belong to multiple teams, you will be prompted to select a team.

---

## 3. Switching Teams

If you are a member of more than one team, click the team name in the top navigation bar. A dropdown shows your available teams. Click any team to switch.

---

## 4. Navigation

| Item | Purpose |
|------|---------|
| **Dashboard** | Your personal task stats |
| **Tasks** | Your team's task list |
| **Sprints** | View current and upcoming sprints |
| **Charts** | Visual analytics for your team |
| **Activity** | Team activity log |

---

## 5. Tasks

### Viewing Tasks

Open **Tasks** from the navigation. Your team's tasks are shown. Switch between views:

| View | Use it for |
|------|-----------|
| **Table** | Full sortable list |
| **Board** | Status columns |
| **Kanban** | Swimlane workflow |
| **Calendar** | Tasks by target date |
| **Backlog** | Unplanned tasks |
| **Team** | Who is working on what |

### Filtering

Click **Show filters** to open the filter bar. Filter by keyword, module, client, owner, status, priority, or date range.

### Creating a Task

1. Click **New Task**.
2. Required: **Goal** (task title) and **Project** (module).
3. Add other details:
   - **Owner** — who will work on this (default: yourself)
   - **Priority** — P0 (Critical) through P3 (Low)
   - **Status** — defaults to New
   - **Client** — hotel property or group
   - **Target Date**
   - **Sprint** — if there is an active sprint
   - **Item Type** — Bug, Feature, Task, etc.
   - **Notes**, **Acceptance Criteria**, **Labels**
4. Click **Save**.

### Updating a Task

Click any task to open it. Update the status, add notes, change dates, or reassign it. Click **Save** when done.

**Tip:** When you start working on a task, change the status to **Active** (or **In Progress**). When finished, change it to **Done** (or **Resolved** for bugs/features). This keeps the team's charts and sprint tracking accurate.

### Deleting a Task

Open a task and click **Delete**. Deleted tasks go to the Recycle Bin. To restore a deleted task, ask your Team Lead, Team Admin, or SysAdmin.

### Bulk Operations

Select multiple tasks using checkboxes:
- **Change Status**
- **Change Owner**
- **Assign Sprint**
- **Change Priority**
- **Delete**

---

## 6. Sprints (View Only)

Open **Sprints** to see:
- The current active sprint and its goal
- Which tasks are in the sprint
- Sprint start and end dates
- Each sprint's completion progress

Sprint management is handled by your Team Admin or SysAdmin.

---

## 7. Dashboard

### My Dashboard

Your personal task summary:
- **Total** — all tasks assigned to you
- **Done** — completed tasks
- **In Progress** — tasks actively being worked on
- **Open** — tasks not yet started
- **Overdue** — tasks past their target date

Also shows recent activity and a breakdown by project.

### Team Dashboard

Click **Team Performance** (top right) to see how the whole team is doing: status distribution, owner workload, and project breakdown.

---

## 8. Charts

The Charts page shows visual breakdowns for your team:
- Velocity per sprint
- Status and priority distribution
- Owner workload
- Project and property breakdown

---

## 9. Activity Log

The Activity page shows a log of all recent changes in your team — who changed what, on which task, and when.

---

## 10. Profile & Notifications

Access your profile via the avatar/name in the top-right corner.

### Updating Your Profile

- **Display Name** — how your name appears on tasks and in the team view
- **Email** — required to receive email notifications
- **Job Title / Department** — optional
- **Avatar** — your profile picture or initials colour

### Changing Your Password

1. Go to your profile.
2. Enter your current password.
3. Enter your new password (minimum 8 characters).
4. Click **Save**.

### Notification Preferences

Turn on or off:
- Notify me when a task is assigned to me
- Notify me when a comment is added to a task I own or watch
- Notify me when a task I own becomes blocked

### Notification Bell

The **bell icon** (🔔) in the top navigation bar shows unread notifications. Click it to see alerts and mark them as read.

### My API Keys

A personal API key lets a tool you use, such as WorkHub, read or update your tasks on your behalf.

1. Open the avatar menu → **My API Keys**.
2. Enter a name (e.g. "WorkHub on my PC"), tick the scopes it needs (`tasks:read` to read; add `tasks:write` only if the tool must create tasks or log time), keep **Only my tasks** ticked, and pick an expiry.
3. Click **Create key** and copy the key straight away — it is shown only once.

A key acts as you, so never share it. Revoke it on the same page as soon as you stop using the tool. Keys expire after at most one year, and you can have up to 10 active keys.

---

## 11. Quick Reference

| What you want to do | Where to go |
|--------------------|-------------|
| See my tasks | Tasks → filter by Owner = me |
| Update a task's status | Click task → change Status → Save |
| Create a new task | Tasks → New Task |
| See the current sprint | Sprints |
| Check team progress | Dashboard → Team Performance |
| Update my profile | Top-right avatar → Profile |
| Change my password | Top-right avatar → Profile → Change Password |
| Turn off notifications | Top-right avatar → Profile → Notifications |

---

## 12. Common Status Values

| Status | Meaning |
|--------|---------|
| **New** | Created, not yet started |
| **To Do** | Planned, ready to start |
| **Active / In Progress** | Currently being worked on |
| **On Hold** | Paused, waiting for something |
| **Blocked** | Cannot proceed — unresolved dependency or issue |
| **Done** | Completed (for Tasks) |
| **Resolved** | Completed (for Bugs, Stories, Features) |
| **Closed** | Verified and closed |
| **Cancelled** | Will not be done |

Update your task status regularly — it helps your team lead and team admin see accurate progress in the dashboard and charts.
