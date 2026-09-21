# Team Admin Manual
## Team Destinity Inspire — Task Tracker

---

## 1. Your Role

As a **Team Admin**, you manage your team's members, sprints, and settings. You have full control over your team's tasks and can create new users directly into your team. You do not have access to other teams or system-wide user management.

---

## 2. Logging In

1. Open the app in your browser.
2. Enter your **username** and **password**.
3. Complete the reCAPTCHA check.
4. Click **Login**.

If you are a member of only one team, you are taken directly to the dashboard. If you belong to multiple teams, you will be prompted to select a team first.

---

## 3. Switching Teams

If you are a member of more than one team, click the team name in the top navigation bar. A dropdown shows your available teams. Click any team to switch — you stay on the same page but the data switches to that team.

---

## 4. Navigation

| Item | Purpose |
|------|---------|
| **Dashboard** | Your personal stats and team performance |
| **Tasks** | Your team's full task list |
| **Sprints** | Sprint planning and management |
| **Charts** | Visual analytics for your team |
| **Activity** | Team activity log |
| **Recycle Bin** | View (but not restore) deleted tasks |

---

## 5. Team Management (`/Teams/Manage`)

Click your team name in the top nav → select your team → if a "Team Settings" link appears, click it. Alternatively, navigate to `/Teams/Manage/{id}`.

### Editing Team Details

In the left panel:
- **Name** — update the team's display name.
- **Description** — short description of the team's purpose.
- **Color** — team brand colour used in badges and charts.

Click **Save Changes**.

### Adding an Existing User

If another system user exists but is not yet on your team:
1. Select their name from the **Add Member** dropdown.
2. Choose a **Team Role** (Member, Team Lead, Team Admin).
3. Click **Add**.

### Creating a New User

To create a brand-new user account and add them to your team at the same time:
1. Click **Show / Hide** next to "Create New User".
2. Fill in:
   - **Full Name** — displayed across the app
   - **Username** — unique login name (lowercase)
   - **Email** — optional, needed for email notifications
   - **Password** — minimum 8 characters
   - **Team Role** — Member or Team Lead (Team Admins cannot create other Team Admins)
3. Click **Create & Add**.

The user account is created system-wide and immediately added to your team.

### Changing Member Roles

Use the role dropdown next to any member's name. Changes take effect immediately. You can assign **Member**, **Team Lead**, or **Team Admin**.

### Removing Members

Click the **×** button next to a member to remove them from the team. The user account is not deleted — they just lose membership of this team.

---

## 6. Sprints (`/Sprints`)

### Creating a Sprint

1. Go to **Sprints** in the navigation.
2. Click **New Sprint**.
3. Enter:
   - **Sprint Name**
   - **Start Date** and **End Date**
   - **Goal** (optional)
4. Click **Save**.

### Managing Sprints

- **Set Active** — marks a sprint as the current working sprint. Only one sprint can be active at a time.
- **Complete Sprint** — closes the sprint. Tasks not completed are moved back to the backlog.
- **Delete Sprint** — permanently removes the sprint. Tasks are not deleted.
- **Capacity Planning** — set each team member's daily available hours for the sprint.

---

## 7. Tasks

### Viewing Tasks

Open **Tasks** from the navigation. Switch between views:

| View | Use it for |
|------|-----------|
| **Table** | Full list, sortable by any column |
| **Board** | Status columns (drag not required) |
| **Kanban** | Swimlane workflow board |
| **Calendar** | Tasks by target date |
| **Backlog** | Sprint planning — assign tasks to sprints |
| **Team** | See what each team member is working on |

### Filtering

Click **Show filters** to open the filter bar. Filter by keyword, module, client, owner, status, priority, or date range.

### Creating a Task

1. Click **New Task**.
2. The canvas opens. Required: **Goal** and **Project**.
3. Fill in other fields as needed:
   - **Client** — the hotel property or group
   - **Owner** — assign to one or more team members
   - **Priority** — P0 (Critical) through P3 (Low)
   - **Status** — New, To Do, Active, On Hold, Done, etc.
   - **Sprint** — assign to an upcoming or active sprint
   - **Target Date**, **Start Date**, **End Date**
   - **Item Type** — Bug, Feature, Task, etc.
   - **Parent Task** — link to a parent for sub-tasks
   - **Weight** — story point estimate
   - **Labels**, **Notes**, **Acceptance Criteria**
4. Click **Save**.

### Editing a Task

Click any task to open the canvas. Edit any field and click **Save**. You can edit any task — not just ones assigned to you.

### Deleting Tasks

Open a task and click **Delete**, or select tasks via checkboxes and use **Bulk Delete**. Deleted tasks go to the Recycle Bin.

### Recycle Bin

As a Team Admin you can **view** deleted tasks. Contact a SysAdmin to restore or permanently delete tasks.

### Bulk Operations

Select multiple tasks using checkboxes:
- **Change Status** — move multiple tasks to a new status
- **Change Owner** — reassign multiple tasks
- **Assign Sprint** — move tasks into a sprint
- **Change Priority** — update priority for a batch
- **Delete** — move to recycle bin

### Exporting Tasks

Use the **Export CSV** button to download the current filtered task list as a spreadsheet.

---

## 8. Dashboard

### My Dashboard

Shows your personal task counts (Total, Done, In Progress, Open, Overdue), completion percentage, recent activity, and your best-performing project.

### Team Dashboard

Click **Team Performance** (top right of My Dashboard) to see team-level statistics:
- Status distribution
- Project breakdown
- Owner workload (who has how many tasks)
- Recent team activity

---

## 9. Charts

The Charts page provides visual breakdowns for your team:
- **Velocity** — completed story points per sprint
- **Status distribution** — proportion of tasks by status
- **Priority breakdown** — how tasks are distributed across P0–P3
- **Owner workload** — task count per team member
- **Project and property distribution**

---

## 10. Activity Log

The Activity page shows every change made within your team: who created, edited, or commented on which task, and when.

---

## 11. Profile & Notifications

Access your profile via the avatar/name in the top-right corner.

- **Update email** — required to receive email notifications.
- **Change password** — minimum 8 characters.
- **Notification preferences:**
  - Notify when a task is assigned to you
  - Notify when a comment is added to a task you own or watch
  - Notify when a task you own becomes blocked

The **notification bell** (🔔) in the top bar shows unread alerts. Click to view and dismiss.

---

## 12. Quick Reference — Team Admin Exclusive Actions

| Action | Location |
|--------|---------|
| Edit team name / colour | Teams → Manage → left panel |
| Add existing user to team | Teams → Manage → Add Member |
| Create new user for team | Teams → Manage → Create New User |
| Change member roles | Teams → Manage → member role dropdown |
| Remove member from team | Teams → Manage → × button |
| Create / complete / delete sprints | Sprints |
| View recycle bin | Recycle Bin (restore via SysAdmin) |
