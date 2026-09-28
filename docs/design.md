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

## Work entries

| Method and path | Needs | Purpose |
|---|---|---|
| `POST /api/work-entries` | `work_entry:create` | Logs the caller's work: `{ projectId, description, startedAt, endedAt, moves }`. Returns `201` |
| `PUT /api/work-entries/{id}` | `work_entry:create` | Replaces project, description and times of one of the caller's entries |
| `DELETE /api/work-entries/{id}` | `work_entry:create` | Soft delete (`deleted_at`). Returns `204` |
| `POST /api/work-entries/{id}/restore` | `work_entry:create` | Undoes a delete |
| `GET /api/work-entries?from=&to=&projectId=&userId=&clientId=&search=&page=1&pageSize=25` | `work_entry:view_all` | Everyone's Design entries, newest first |
| `GET /api/me/work-entries?from=&to=&projectId=&search=&page=1&pageSize=25` | `work_entry:view_own` | The caller's entries, newest first |
| `GET /api/work-entries/{id}` | `work_entry:view_all`, or `work_entry:view_own` for your own | The entry, its comments, and the same person's other entries on the project that day |

People log, edit, delete and restore only their **own** entries; anyone else's reads as `404`, whatever the caller may view. The rules are in `Business/WorkEntries/WorkEntryRules.cs`:

- **Projects:** Design only (`422 not_for_this_domain`). A project accepts work while it is active, or on hold when `holdLogging` is on (`422 project_not_accepting_work`). Completed and archived projects never do.
- **Times** are UTC instants in whole minutes; a time without an offset is read as UTC. An entry is 5 minutes to 16 hours long (`400`). It belongs to the company-time-zone day it starts on, and may run past midnight.
- **Future:** an entry may not end after now unless `allowFutureEntries` is on (`422 entry_in_future`).
- **Description:** required while `requireDescription` is on (`400 description_required`). At most 2000 characters.
- **Overlap:** a person's entries may not overlap, unless `allowOverlap` is on (`422 entry_overlaps`). Back-to-back entries are fine. Each change locks the person's user row, so two simultaneous changes are checked one after the other.
- **Changing an entry** (edit, delete, restore) is allowed within the edit window, counted from when it was logged (edits don't extend it), and only while its project still accepts work (`422 edit_window_closed`, `422 project_not_accepting_work`). Responses carry `canEdit` for the caller.
- **`moves`** saves the timeline's swap: other entries of the caller moved to make room, `[{ id, startedAt, endedAt }]` (at most 20). They are checked with the new entry and saved in the same transaction, or not at all.
- The timer runs in the client; stopping it logs an entry through `POST /api/work-entries`.
- Logging never adds the person to the project's members.

**Events:** `work_entry_logged`, `work_entry_updated` (also once per moved entry), `work_entry_deleted`, `work_entry_restored`, with the entry as the subject and its minutes in the payload.

**Notifications** go to the active people who see everyone's work in Design (`work_entry:view_all`), never the author, following `notifyEntry`:

| `notifyEntry` | Sends |
|---|---|
| `all` | One `work_logged` notification per entry (author, project, minutes and description in the payload) |
| `long` | The same, only for entries of 3 hours or more |
| `digest` | One `work_digest` notification per recipient per company day (digest key `work-digest-YYYY-MM-DD`). Each entry adds 1 to `count`, adds its minutes to the payload's `minutes`, and marks it unread again |
| `off` | Nothing |

**Reading:** `from` and `to` are company-time-zone days, both inclusive, matched on the start time. `search` matches the description or project title (every word). Each entry carries the person, the project, `minutes`, `commentCount` and `canEdit`.

## Comments

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/work-entries/{id}/comments` | Whoever can read the entry | Oldest first, with the author's name and role |
| `POST /api/work-entries/{id}/comments` | `work_entry:comment` | `{ body, sendToGroup }`. Returns `201` |

- A comment is 1 to 2000 characters. Comments can't be edited or deleted.
- `sendToGroup` (also post to the author's team group chat) is refused with `422 group_chat_unavailable` until group chats exist.
- The entry's author gets an `entry_comment` notification (commenter's name, the text and the entry's description), unless they wrote it themselves.
- Event: `work_entry_commented`, with the entry as the subject and the comment id in the payload.
