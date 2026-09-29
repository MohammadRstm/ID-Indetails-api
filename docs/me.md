# Self-service (`/api/me`)

What signed-in people see about themselves. These endpoints only ever return the caller's own data and never take a user id. For now they serve Design people: each needs `work_entry:view_own`, which only people of the Design domain hold. The caller's work entries, summary, projects and day are in [design.md](design.md); their profile is in [employees.md](employees.md).

## My requirements

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/requirements` | `work_entry:view_own` | The caller's open requirements, soonest deadline first |

Rules (`Business/Requirements/MyRequirements.cs`):

- The requirements assigned to the caller on projects they can see that are active or on hold. Completed and archived projects are left out.
- Open ones are listed soonest deadline first; those without a deadline come last, oldest first. Each has `isOverdue` (open with a deadline before today), `daysLeft` (negative once overdue, null without a deadline), its project, and everyone else it is assigned to.
- Counts: `projects` with at least one of them, `overdue`, `dueSoon` (a deadline from today to 7 days ahead) and `done` (already done, not listed).

## My schedule

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/schedule` | `work_entry:view_own` | The caller's week, the company policy in force, and today's hours |

- `schedule`: the caller's week as it stands today, with the same shape as the Schedules screen. It is their own schedule where they have one (`isOwn`, `ownSince`), otherwise the company week. Each working day has its start, end and required minutes, and `nextChangeOn` is a change dated in the future.
- `companyWeek`: the company policy in force today. Its break and overtime multiplier apply to everyone.
- `today`: whether today is a working day, its hours, required minutes and paid break, and what the caller has logged today. `insideMinutes` is logged time that isn't overtime, and `leftMinutes` is what the day still requires. On a day off everything logged is overtime.
