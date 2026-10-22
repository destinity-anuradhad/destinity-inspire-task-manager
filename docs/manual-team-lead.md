# Team Lead Manual
## Team Destinity Inspire — Task Tracker

---

## 1. Your Role

As a **Team Lead**, you manage day-to-day task work for your team. You can create tasks, assign work to team members, and add new members to your team. You do not manage sprints, team settings, or system users.

---

## 2. Logging In

1. Open the app in your browser.
2. Enter your **username** and **password**.
3. Complete the reCAPTCHA check.
4. Click **Login**.

If you belong to only one team, you are taken directly to the dashboard. If you belong to multiple teams, you will be prompted to select a team first.

---

## 3. Switching Teams

If you are a member of more than one team, click the team name in the top navigation bar. A dropdown shows your available teams. Click any team to switch.

---

## 4. Navigation

| Item | Purpose |
|------|---------|
| **Dashboard** | Your personal task stats and team performance |
| **Tasks** | Your team's full task list |
| **Sprints** | View sprints (read-only) |
| **Charts** | Visual analytics for your team |
| **Activity** | Team activity log |

---

## 5. Adding Team Members

Team Leads can add new members to their team from the team's Manage page.

### Adding a New User to the Team

1. Ask your Team Admin to navigate to Teams → Manage, **or** if you have the link, go to `/Teams/Manage/{id}`.
2. In the **Create New User** section, fill in:
   - **Full Name**
   - **Username** — unique login name (lowercase)
   - **Email** — optional
   - **Password** — minimum 8 characters
3. Click **Create & Add**.

The new user is always added as a **Member** — Team Leads cannot create or assign Team Lead or Team Admin roles.

> **Note:** Team Leads can create new users but cannot change existing member roles, remove members, or edit team settings. Contact your Team Admin for those actions.

---

## 6. Tasks

### Viewing Tasks

Open **Tasks** from the navigation. Switch between views using the toolbar at the top:

| View | Use it for |
|------|-----------|
| **Table** | Full list, sortable by any column |
| **Board** | Status columns |
| **Kanban** | Swimlane workflow board |
| **Calendar** | Tasks laid out by target date |
| **Backlog** | Tasks not yet in a sprint |
| **Team** | Who is working on what |

### Filtering

Click **Show filters** to open the filter bar. Filter by keyword, module, client, owner, status, priority, or date range.

### Creating a Task

1. Click **New Task**.
2. The canvas opens. Required fields: **Goal** and **Project**.
3. Fill in other fields:
   - **Client** — hotel property or group
   - **Owner** — assign to team members
   - **Priority** — P0 (Critical) through P3 (Low)
   - **Status** — defaults to New
   - **Sprint** — assign to a sprint
   - **Target Date**, **Start Date**, **End Date**
   - **Item Type** — Bug, Feature, Task, etc.
   - **Parent Task** — link as a sub-task
   - **Weight** — story point estimate
   - **Labels**, **Notes**, **Acceptance Criteria**
4. Click **Save**.

### Editing a Task

Click any task to open the canvas. Change any field and click **Save**.

### Deleting a Task

Open a task and click **Delete**, or check multiple tasks and use **Bulk Delete**. Deleted tasks go to the Recycle Bin — contact a SysAdmin or Team Admin if you need a task restored.

### Bulk Operations

Select multiple tasks using checkboxes:
- **Change Status**
- **Change Owner**
- **Assign Sprint**
- **Change Priority**
- **Delete**

### Exporting Tasks

Click **Export CSV** to download the current filtered task list as a spreadsheet.

---

## 7. Sprints (View Only)

Go to **Sprints** to see the current and upcoming sprints, their start and end dates, goals, and which tasks are assigned to each sprint.

Sprint creation, completion, and deletion are managed by your Team Admin or SysAdmin.

---

## 8. Dashboard

### My Dashboard

Shows your personal stats: total assigned tasks, done, in progress, open, and overdue. Also shows recent activity and your best-performing project.

### Team Dashboard

Click **Team Performance** (top right of My Dashboard) to see:
- Status distribution across the team
- Project breakdown
- Owner workload
- Recent team activity

---

## 9. Charts

The Charts page shows visual breakdowns for your team:
- Velocity per sprint
- Status and priority distribution
- Owner workload
- Project and property breakdown

---

## 10. Activity Log

The Activity page shows every change made within your team — who changed what and when.

---

## 11. Profile & Notifications

Access your profile via the avatar/name in the top-right corner.

- **Update email** — required for email notifications.
- **Change password** — minimum 8 characters.
- **Notification preferences:**
  - Notify when a task is assigned to you
  - Notify when a comment is added to a task you own or watch
  - Notify when a task you own becomes blocked

The **notification bell** (🔔) in the top bar shows unread alerts.

---

## 12. Quick Reference — What Team Leads Can Do

| Action | Location |
|--------|---------|
| View all team tasks | Tasks |
| Create / edit / delete tasks | Tasks → New Task or task canvas |
| Assign tasks to team members | Task canvas → Owner field |
| Bulk status / owner / sprint changes | Tasks → checkboxes |
| Add a new Member-level user | Teams → Manage → Create New User |
| View sprints | Sprints (read-only) |
| View team dashboard | Dashboard → Team Performance |
| Export tasks | Tasks → Export CSV |

---

## 13. What Team Leads Cannot Do

- Create or manage sprints
- Edit team name, colour, or description
- Remove members from the team
- Change member roles
- Access Master Files (system user management)
- Restore or permanently delete tasks from the Recycle Bin
- Access other teams' data
