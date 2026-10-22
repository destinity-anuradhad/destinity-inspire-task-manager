# System Administrator Manual
## Team Destinity Inspire — Task Tracker

---

## 1. Your Role

As a **System Administrator (SysAdmin)**, you have unrestricted access to the entire application. You manage users, teams, and system settings. You can view and act on data across all teams simultaneously (Global View) or switch into any individual team to work alongside its members.

---

## 2. Logging In

1. Open the app in your browser.
2. Enter your **username** and **password**.
3. Complete the reCAPTCHA check.
4. Click **Login**.

If this is your first login with a system-assigned password, you will be prompted to set a new password immediately before proceeding.

---

## 3. Global View vs. Team View

After login you are placed in **Global View** — you see tasks and data across all teams simultaneously.

To switch into a specific team, click your name/team indicator in the top navigation bar and select a team from the dropdown. The page you are on stays open; only the data scope changes.

To return to Global View, open the same dropdown and select **Global View**.

> **Tip:** Use Global View for system-level tasks (user management, team creation). Switch into a team when you want to work on tasks or sprints alongside that team.

---

## 4. Navigation

The top navigation bar contains:

| Item | Purpose |
|------|---------|
| **Dashboard** | Personal task stats and, in team view, team performance |
| **Tasks** | Full task list with all views and filters |
| **Sprints** | Sprint planning and management |
| **Charts** | Visual analytics for the current scope |
| **Teams** | Manage all teams (SysAdmin only) |
| **Master** | User accounts and system settings (SysAdmin only) |
| **Activity** | System-wide activity log |
| **Recycle Bin** | Deleted tasks — restore or permanently remove |

---

## 5. Master Files (`/Master`)

Master Files is your primary user management area.

### Viewing Users

All registered users are listed with their display name, username, email, system role (SysAdmin or Member), status (Active / Pending / Rejected), and current team assignments.

### Creating a New User

1. Click **New User** (top right).
2. Fill in:
   - **Full Name** — displayed throughout the app
   - **Username** — used for login (lowercase, unique)
   - **Email** — optional; required for email notifications
   - **Password** — minimum 8 characters (user must change on first login)
   - **System Role** — Member (default) or SysAdmin
   - **Assign to Team** — optionally place the user in a team immediately with a chosen role
   - **Avatar Color** — pick a colour for the user's initials avatar
3. Click **Create User**.

The account is created as Active immediately — no approval step.

### Editing a User

Click the **edit icon** (pencil) on any user row. You can change:
- Display name
- Username
- System role (Member ↔ SysAdmin)
- Avatar colour

---

## 6. Team Management (`/Teams`)

The Teams list is visible only to SysAdmins.

### Creating a Team

1. Go to **Teams**.
2. Click **New Team**.
3. Enter the team name, optional description, and brand colour.
4. Click **Create**. You are taken to the Manage page for the new team.

### Managing a Team (`/Teams/Manage/{id}`)

From any team's Manage page you can:

**Edit team details** — name, description, colour (left panel).

**Add existing users** — select any active system user from the dropdown and assign a team role (Member, Team Lead, or Team Admin). Click **Add**.

**Create a new user directly into the team** — expand the "Create New User" section, fill in the details, and the user is created system-wide and added to this team in one step.

**Change member roles** — use the role dropdown next to any member name; change takes effect immediately.

**Remove members** — click the × button next to a member.

**Deactivate / Reactivate a team** — available only to SysAdmins via the "Deactivate Team" button at the bottom of the team info panel. Deactivated teams are hidden from team selection.

---

## 7. Tasks

### Viewing Tasks

The Tasks page (`/Tasks`) opens in **Table** view by default. Use the view switcher at the top to change to:

| View | Best for |
|------|---------|
| **Table** | Sorting and scanning all tasks |
| **Board** | Status columns (Kanban-style) |
| **Kanban** | Swimlane-based workflow |
| **Calendar** | Deadline-oriented scheduling |
| **Backlog** | Sprint planning |
| **Team** | Who is working on what |

### Filtering Tasks

Click **Show filters** (top of the task list) to open the filter bar. You can filter by:
- Keyword search
- Module / Project
- Client / Property
- Assigned owner
- Status, Priority
- Target date range

Filters remain active while you navigate between views. The filter bar auto-opens if any filter is set in the URL.

### Creating a Task

1. Click **New Task** (top right on the Tasks page).
2. The task canvas opens. Fill in at minimum:
   - **Goal** — the task title/description
   - **Project** — the module this task belongs to
   - **Status** — defaults to New
3. Optional fields: Client, Priority, Owner(s), Target Date, Start Date, Sprint, Item Type, Parent Task, Weight, Labels, Notes, Acceptance Criteria, Reporter.
4. Click **Save**.

### Editing a Task

Click any task row to open the canvas. All fields are editable. Changes are saved when you click **Save**.

### Deleting and Restoring Tasks

- To delete: open the task canvas and click **Delete**, or use the checkbox + **Bulk Delete**.
- Deleted tasks go to the **Recycle Bin** — they are not permanently removed.
- In the Recycle Bin you can **Restore** or **Permanently Delete** individual tasks, or **Empty the Bin** to permanently remove all deleted tasks.

> Only SysAdmins can restore or permanently delete from the Recycle Bin. Team Admins can view the bin but not restore or permanently delete.

### Bulk Operations

Check the checkboxes next to multiple tasks to access bulk actions:
- Change Status
- Change Owner
- Assign to Sprint
- Change Priority
- Delete

---

## 8. Sprints

Go to **Sprints** in the navigation.

- **Create** a new sprint with name, start date, end date, and optionally a goal.
- **Set Active** — only one sprint can be active at a time.
- **Complete Sprint** — closes the sprint and moves incomplete tasks to backlog.
- **Manage Capacity** — set daily capacity per team member.
- **Delete Sprint** — permanently removes the sprint record.

---

## 9. Dashboard

### My Dashboard

Shows your personal task statistics: total, done, in progress, open, overdue tasks, and recent activity.

### Team Dashboard

When in a team view, click **Team Performance** (top right of My Dashboard) to see team-level stats: status distribution, project breakdown, owner workload, recent activity.

### All Teams Dashboard (`/Dashboard/Teams`)

Available only in Global View. Shows a high-level performance summary across all teams.

---

## 10. Charts

The Charts page (`/Charts`) shows:
- Task velocity over sprints
- Status distribution pie chart
- Priority breakdown
- Owner workload
- Project and property distribution

All charts are scoped to the currently active team (or global in Global View).

---

## 11. Activity Log

The Activity page shows a full audit trail of all changes: who did what, when, on which task. Filterable by date and action type.

---

## 12. Profile & Notifications

Go to your **avatar/name** in the top-right to access your profile.

- **Email** — update your email address (used for notifications).
- **Change Password** — enter current password and set a new one (min 8 characters).
- **Notification Preferences** — enable or disable:
  - Notify when a task is assigned to you
  - Notify when a comment is added to a task you own/watch
  - Notify when a task you own is blocked

The **notification bell** (🔔) in the top bar shows unread notifications. Click it to view the list and mark items as read.

---

## 13. Quick Reference — SysAdmin Exclusive Actions

| Action | Location |
|--------|---------|
| Create / edit users | Master Files |
| Create / deactivate teams | Teams |
| View all teams | Teams |
| Restore deleted tasks | Recycle Bin |
| Permanently delete tasks | Recycle Bin |
| Empty recycle bin | Recycle Bin |
| Switch to Global View | Team switcher (top nav) |
| View all-teams dashboard | Dashboard → Teams (Global View) |
