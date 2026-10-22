# Role & access matrix

The single reference for what each role can do. Enforcement is consistent across the navigation
(`_Layout.cshtml`), controllers (`AppControllerBase` helpers), and services (`UserCtx`).

## Roles

| Role | Source | Meaning |
|---|---|---|
| **System Admin** | `AppUser.Role == "Admin"` | Full access to every team and all administration. |
| **Team Admin** | `TeamMember.TeamRole == "TeamAdmin"` | Full management within their active team. |
| **Team Lead** | `TeamMember.TeamRole == "TeamLead"` | Can assign and move others' work within the team. |
| **Member** | any other team role | Works on their own / owned items. |

`UserCtx` composes these: `IsTeamAdmin = SysAdmin || TeamAdmin`; `IsTeamLead = IsTeamAdmin ||
TeamLead`; `IsMember = not any of the above`.

## Capabilities

| Capability | Member | Team Lead | Team Admin | System Admin |
|---|:--:|:--:|:--:|:--:|
| View team work | ✅ | ✅ | ✅ | ✅ (all teams) |
| Create tasks | ✅ | ✅ | ✅ | ✅ |
| Edit / status-change **own or owned** task | ✅ | ✅ | ✅ | ✅ |
| Edit / status-change **any** team task | — | via assign | ✅ | ✅ |
| Assign owners / move others' work | — | ✅ | ✅ | ✅ |
| Bulk operations | own only | ✅ | ✅ | ✅ |
| Manage sprints / workflows / projects | — | partial | ✅ | ✅ |
| Manage API keys, webhooks, import, **automation**, **SLA** | — | — | ✅ | ✅ |
| Manage teams & master data | — | — | — | ✅ |

Key enforcement points:

- **Named policies** — the coarse role gates are three central, tested policies backed by
  `TeamAuthorization` (`Services/TeamAuthorization.cs`) and registered in `Program.cs`:

  | Policy | Requires | Applied to |
  |---|---|---|
  | `Policies.SystemAdmin` | System Admin | Activity, Master, EmailOutbox |
  | `Policies.TeamAdmin` | Team Admin+ | API Keys, Webhooks, Import, Automations, SLA; Sprint delete |
  | `Policies.TeamLead` | Team Lead+ | Sprint create/activate/complete/capacity |

  Controllers apply them declaratively (`[Authorize(Policy = …)]`) instead of duplicating imperative
  `CanManage()` checks.
- **Nav** — admin surfaces (API Keys, Webhooks, Import, Automations, SLA) render for `Admin ||
  TeamRole == "TeamAdmin"`; Teams/Master render for System Admin only.
- **Services (per-resource)** — role isn't enough for resource-level actions: `CanModifyAsync` allows a
  Team Admin, the task's creator, or an owner; `CanAssign` requires `IsTeamLead`. Status transitions
  additionally obey the governing workflow/`TaskStatusRules`. These stay in the domain services by
  design — they depend on the specific task, not just the caller's role.
- **Team scope** — every read/write is constrained to the caller's active team (`ActiveTeamId`),
  except System Admin.
