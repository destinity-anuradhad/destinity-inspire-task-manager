# User onboarding

## Product decision

**Self-service registration is disabled.** Users are **provisioned by administrators**, never created
by anonymous visitors. This keeps account creation governed (right team, right role, active status)
and removes the public registration attack surface.

There is intentionally **no `Register` action** — `/Account/*` exposes only an anonymous `Login`; any
attempt to reach a registration URL falls through to login.

## How a user is created

An administrator provisions a user from **Teams → Manage → Create user**
(`TeamsController.CreateUser` → `TeamService.CreateTeamUserAsync`):

1. **Authorization.** Only a System Admin, Team Admin, or Team Lead of the team may create users. Role
   escalation is capped: a Team Lead can only create Members; a Team Admin cannot mint another Team
   Admin. (System Admin has no cap.)
2. **Validation.** Display name, username, and password are required; the password must be ≥ 8
   characters; the username must be unique.
3. **Account.** The user is created `Active`, added to the team with the granted role, and flagged
   **`MustChangePassword`** — so the admin-chosen password is a one-time value.
4. **Audit.** A `User Provisioned` activity entry records who created whom, with which role, in which
   team.

## First login — forced password reset

On first login the `MustChangePassword` claim redirects the user to **`/Account/ForceChangePassword`**,
which blocks all other pages until they set a new password; the flag is then cleared and the cookie
re-issued (`AccountController.ForceChangePassword`). Because the account is provisioned with the flag
set, the initial admin-chosen password can never be used for normal work.

## Status & access

Account status is enforced app-wide: a non-`Active` user cannot log in, and membership/role changes
invalidate existing sessions (see the session-revocation work, backlog #5). Direct-URL access is gated
by the named authorization policies (see [`role-matrix.md`](role-matrix.md)).

## Not in scope (by decision)

A public "request an account" approval queue is intentionally **not** built — provisioning is the
governed path. `AppUser.RequestedTeamId` exists for team-join requests within an existing account and
is separate from account creation.
