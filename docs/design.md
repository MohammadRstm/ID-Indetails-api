# Design work

Features of the Design domain: domain settings, work entries, comments, meetings, and the work views and reports built on them. A record of a domain the caller can't see reads as `404`, for every endpoint below.

## Domain settings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/domain-settings/{domain}` | Signed in, able to see the domain | The domain's time-tracking and notification rules. Employees read them too |
| `PUT /api/domain-settings/{domain}` | `domain_settings:manage` | Replaces every setting |

- No rows are seeded. Until a domain's admins first save, `GET` returns the defaults with `updatedAt: null`.
- Every save writes `domain_settings_updated`.

| Setting | Default | Values | Effect |
|---|---|---|---|
| `editWindow` | `one_day` | `none`, `one_day`, `seven_days`, `unlimited` | How long after it was logged an entry may be edited, deleted or restored |
| `allowOverlap` | `false` | | Whether a person's entries may overlap |
| `allowFutureEntries` | `false` | | Whether an entry may end after now |
| `requireDescription` | `true` | | Whether an entry needs a description |
| `timerEnabled` | `true` | | Shown by the client (the timer runs in the client) |
| `logReminder` | `true` | | Shown by the client (the afternoon reminder) |
| `notifyEntry` | `all` | `all`, `long` (3 hours or more), `digest`, `off` | Notifications to admins when work is logged |
| `notifyProject`, `notifyIdle`, `weeklySummary`, `autoArchive` | `true`, `true`, `true`, `false` | | Stored only for now; the jobs that use them come later |
| `holdLogging` | `false` | | Whether work may still be logged on a project on hold |
