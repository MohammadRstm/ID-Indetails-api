# Design work

Features of the Design domain: domain settings, work entries, comments, meetings, and the work views and reports built on them. Most of these endpoints need a Design-only permission (such as `work_entry:view_all`), so a caller without it, for example an Execution admin, gets `403` before any record is looked up. A caller who holds the permission but asks for a record of a domain they can't see gets `404`.

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
| `notifyIdle` | `true` | | Whether the dashboard flags projects with no work for 7 days, and their owners and managers get a `project_idle` notification (see [notifications.md](notifications.md)) |
| `notifyProject` | `true` | | Whether the domain's people hear about new projects, status changes and added requirements (see [notifications.md](notifications.md)) |
| `weeklySummary` | `true` | | Whether everyone who logs work gets last week's `weekly_summary` on the first day of the week |
| `autoArchive` | `false` | | Archives completed projects nothing has happened on for 90 days (see [projects.md](projects.md), "Status") |
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
| `GET /api/work-entries/export?from=&to=&projectId=&userId=&clientId=&search=` | `work_entry:view_all` | The matching entries as a CSV timesheet (below) |
| `GET /api/work-entries/{id}` | `work_entry:view_all`, or `work_entry:view_own` for your own | The entry, its comments, and the same person's other entries on the project that day |

People log, edit, delete and restore only their **own** entries; anyone else's reads as `404`, whatever the caller may view. The rules are in `Business/WorkEntries/WorkEntryRules.cs`:

- **Projects:** Design only (`422 not_for_this_domain`). A project accepts work while it is active, or on hold when `holdLogging` is on (`422 project_not_accepting_work`). Completed and archived projects never do.
- **Times** are UTC instants in whole minutes; a time without an offset is read as UTC. An entry is 5 minutes to 16 hours long (`400`). It belongs to the company-time-zone day it starts on, and may run past midnight.
- **Future:** an entry may not end after now unless `allowFutureEntries` is on (`422 entry_in_future`).
- **Description:** required while `requireDescription` is on (`400 description_required`). At most 2000 characters.
- **Overlap:** a person's entries may not overlap, unless `allowOverlap` is on (`422 entry_overlaps`). Back-to-back entries are fine. Each change locks the person's user row, so two simultaneous changes are checked one after the other.
- **Changing an entry** (edit, delete, restore) is allowed within the edit window, counted from when it was logged (edits don't extend it), and only while its project still accepts work (`422 edit_window_closed`, `422 project_not_accepting_work`). Responses carry `canEdit` for the caller.
- **Past months never change.** Work that starts before the current company month (before the 1st, company time zone) can't be logged, edited, moved, deleted or restored, by anyone (`422 month_closed`). An edit or a timeline move is refused when either the entry's current start or its new one is in a closed month. Excusing hours is how a closed month is corrected. `canEdit` is false for such entries. The known exception is pay: a pay change dated back into a closed month still changes that month's salary.
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

**Reading:** `from` and `to` are company-time-zone days, both inclusive, matched on the start time. `search` matches the description or project title (every word). Each entry carries the person, the project, `minutes`, `overtimeMinutes`, `commentCount` and `canEdit`.

**Overtime:** an entry's `overtimeMinutes` is its part of the person's overtime that day; the rest of its minutes are normal time. It comes from the rule pay uses (`Business/Payroll/Overtime.cs`, see [company.md](company.md)), through `IOvertimeService`, which loads the person's whole day: time within hours beyond what the day requires counts against the day's latest work, so an entry's overtime depends on the rest of that person's day, on every project.

**CSV export** (`work-entries-<from>-to-<to>.csv`): UTF-8 with a byte-order mark and CRLF line ends, oldest first, one row per entry. Columns: Date, Weekday, Start, End (company time zone, in the company's 12- or 24-hour format), Hours (2 decimals), Overtime hours (2 decimals, as above), Employee, Email, Team, Project, Client (`Internal` for internal projects), Task, then **Hourly rate, Currency, Labour cost** only for callers with `financial:view`, then Entry id. The rate is the person's hourly cost in force on the day worked, in its own currency (`ProjectFinance.RateOn`, the same rule as project cost); the cost is hours × rate, rounded to cents. Exports use the `export` rate-limit policy: 10 a minute per user (`RateLimiting:ExportPermitsPerMinute`).

## My work and a person's summary

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/work-summary?from=&to=&projectId=&search=` | `work_entry:view_own` | The caller's totals and series (below) |
| `GET /api/employees/{userId}/work-summary?from=&to=&projectId=&search=` | `work_entry:view_all` | The same for someone else (their profile). A person of a domain the caller can't see reads as `404` |
| `GET /api/employees/{userId}/labour-cost` | `financial:view` | Their profile's "cost to the company": the labour cost of their work this quarter so far and all time, and the 8 projects it cost most on (all time), priced like project cost (paid breaks included) |
| `GET /api/me/projects` | `work_entry:view_own` | Every active and on-hold Design project, with the caller's own time on it |
| `GET /api/me/projects/{id}/work` | `work_entry:view_own` | The caller's work on one project and the team's latest entries there |
| `GET /api/me/day?date=` | `work_entry:view_own` | One day (today by default): the caller's entries and the domain's meetings, earliest first, for the day timeline |

Everything is calculated from the entries on each request (`Business/WorkEntries/WorkStats.cs`); nothing is stored. An entry counts on the company-time-zone day it starts on, and weeks start on the company's week start (`company_settings.week_start`, Monday until the company settings exist).

- **Summary:** today, each day of this week, this week, the whole of last week, this month, this quarter, and last quarter up to the same number of days (for a fair comparison); all-time minutes (with `totalOvertimeMinutes`), entries, first day and projects; active days and the longest run of consecutive active days in the last 365 days; the last 16 weeks; every active day of the last 365 days (for the heat map); and time per project (with each project's `overtimeMinutes`). `from`, `to`, `projectId` and `search` narrow only the time per project.
- **My projects:** every open project, as the prototype shows them to employees (not only those they are members of): whether it accepts work, the caller's minutes and latest start, the people who logged on it (most time first, names only), and the latest activity. Other people's hours stay private. Projects the caller worked on come first, most recent first, then the newest.
- **My project work:** the caller's minutes (with `myOvertimeMinutes`), entries and minutes this week on the project; how many people logged there and the project's total minutes; the caller's 12 latest entries; the caller's last 8 weeks there; and the 6 latest entries by other people (name, start and description, as the prototype's "Team activity").
- **My day:** the day's logged minutes with `overtimeMinutes`, and each entry with its own.
- **Overtime** figures are the overtime part of the minutes next to them (see "Overtime" above); the rest is normal time. The prototype shows them as normal and overtime columns.

## Everyone's work by project and person

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/work?from=&to=&search=` | `work_entry:view_all` | A Design project's work (the admin project page) |
| `GET /api/projects/{id}/work/people/{userId}?from=&to=&search=` | `work_entry:view_all` | One person's work on the project (the contributor drawer). `404` if they have none |
| `GET /api/projects/{id}/timeline` | `work_entry:view_all` | The project's history day by day (the admin Timeline tab) |
| `GET /api/work/people?search=&teamId=` | `work_entry:view_all` | Everyone of the Design domain who isn't deactivated, with their hours (the Employees page), most this week first |

- **Project work:** total minutes (with `totalOvertimeMinutes`), entries, people and latest start; the activity level from the last 14 days (busy from 45 h a week, steady from 15 h, quiet with any work, otherwise none) with the last 14 and previous 14 days' minutes; weekly minutes from the project's start (4 to 30 weeks, ending today or on completion); minutes per weekday and hour of the day, each entry spread over the hours it covers; and the contributors within `from`/`to` whose work matches `search` (entries, minutes with `overtimeMinutes`, share of what matched, latest start, last 8 weeks). `search` is the prototype's "filter work text": every word must be in the description. It narrows only the contributors.
- **Contributor:** within the same `from`, `to` and `search`: minutes with `overtimeMinutes`, entries, share of all the project's minutes, last 10 weeks, and the 40 latest entries.
- **Timeline:** every company day with work or a meeting, from the project's start to today (or its completion). Work that runs past midnight counts on each day it covers. Each day has its minutes, the people who worked (most first) and the meetings. It also lists the project's status changes and progress (`project_created`, `project_hold`, `project_resumed`, `project_completed`, `project_archived`, `project_reopened`, `progress_set` with the new progress), oldest first, and the quiet gaps: runs of 4 days or more without work. The client draws the timeline from these.
- **People:** each person's minutes this week, this quarter and in total, entries, latest start, focus (the project with the most time in the last 14 days) and last 8 weeks. `search` matches name, email or position.
- **Labour cost** (contributors and people) is only returned to callers with `financial:view`. It comes from the shared finance calculator (`ProjectFinance.PriceLabour` and `ToUsd`): each hour at the person's hourly cost in force that day, in USD at that day's exchange rate, with any unconverted amount in its own currency and the minutes of people with no pay record counted apart. It therefore always agrees with the project's cost.

## Comments

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/work-entries/{id}/comments` | Whoever can read the entry | Oldest first, with the author's name and role |
| `POST /api/work-entries/{id}/comments` | `work_entry:comment` | `{ body, sendToGroup }`. Returns `201` |

- A comment is 1 to 2000 characters. Comments can't be edited or deleted.
- `sendToGroup` also posts the comment to the entry author's team chat, as a card mentioning everyone in it (see [messaging.md](messaging.md)). An author in no team gives `422 no_team_chat`.
- The entry's author gets an `entry_comment` notification (commenter's name, the text and the entry's description), unless they wrote it themselves.
- Event: `work_entry_commented`, with the entry as the subject and the comment id in the payload.

## Meetings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/meetings?projectId=&mine=&from=&to=` | `project:view` | Meetings of every project the caller can see, earliest first. `mine=true` keeps those the caller set; `from`/`to` are company days a meeting overlaps |
| `GET /api/projects/{id}/meetings` | `project:view` | A project's meetings, earliest first |
| `POST /api/projects/{id}/meetings` | `meeting:create` or `meeting:manage` | `{ startsAt, endsAt, topic, description }`. Returns `201` |
| `PUT /api/meetings/{id}` | `meeting:manage`, or `meeting:create` for your own | Moves, resizes or renames a meeting (the same body) |
| `DELETE /api/meetings/{id}` | `meeting:manage`, or `meeting:create` for your own | Soft delete (`deleted_at`). Returns `204` |

Rules (`Business/Meetings/MeetingRules.cs`):

- Meetings belong to projects of either domain that are active or on hold (`422 project_closed`). Execution sites have them too (decision D10, reversed on 29 Sep 2026).
- Times are UTC instants in whole minutes. A meeting lasts at least 5 minutes and starts and ends on the same company-time-zone day (`400`). Past days are allowed.
- Topic up to 200 characters, description up to 2000; both optional.
- A meeting has no attendees: it is a project-wide block. It is not work time and never counts in hours.
- Events: `meeting_created`, `meeting_updated`, `meeting_deleted`, with the meeting as the subject and its topic and times in the payload.

**Who sets meetings:** everyone holds `meeting:create` by default, in both domains, so employees set meetings and move or remove the ones they set (someone else's is `403`). `meeting:manage` (admins and super admins) covers every meeting. When someone without `meeting:manage` sets a meeting, the active people of the domain who hold it (not the setter) get a `meeting_scheduled` notification with the setter's name, the project, topic and times, as in the prototype.

## Reports and dashboard

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/reports/work?from=&to=&projectId=&userId=` | `report:view` | The Reports page for a range of company days (default: this quarter so far; at most three years) |
| `GET /api/reports/overview?period=quarter\|month\|last30` | `report:view` | The Design dashboard for a period and the one before it |

Callers who can't see Design (an Execution admin holds `report:view` too) get `404`. Money is only included for callers with `financial:view`, and comes from the shared finance calculator.

- **Work report:** total minutes, entries, projects, people, the average entry and the busiest week; weekly minutes (3 to 20 weeks ending with the range); minutes by project (status, entries, people, share, weekly), by client (`null` for internal work) and by person; people against the six busiest projects; Design projects completed in the range (their dates, days taken, and all their minutes and people, whatever the filters); the last 6 quarters of all Design work (whatever the range and filters).
- **Money** (`financial:view`): labour cost of the range's work; expenses of every Design project dated in the range, in USD (whatever the project and person filters, as in the prototype); their total; the all-time profit of every client project; revenue, cost and profit of each project worked on in the range; and labour cost by person. The prototype's monthly "revenue against cost" chart needs past progress, which isn't kept, so it isn't returned.
- **Overview:** the period so far against the one before (quarter to date against the same number of days of last quarter, month to date against the whole last month, the last 30 days against the 30 before): minutes, entries, the average entry, weekly minutes of both, how many active people logged this week, minutes by project (top 8), projects by status, and the busiest project this week.
- **Needs attention** (at most 5, in this order): active projects with no work for 7 days or more (only while `notifyIdle` is on), active projects at 90% or more, projects on hold (with the days since they were put on hold), and how many employees haven't logged this week.
- Overtime columns come once the working-time engine exists.

## Activity feed

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/activity?group=&domain=&projectId=&clientId=&userId=&page=1&pageSize=40` | `activity:view` | Events of the domains the caller can see, newest first. `domain` (`design` or `execution`) keeps that domain's events only, without company-level ones; a domain the caller can't see gives an empty page |
| `GET /api/activity/today` | `activity:view` and `work_entry:view_all` | Design work that started today: minutes, entries, people, and minutes by the hour they were logged |

- **Groups** (the filter chips, `Business/Activity/ActivityGroups.cs`): `work` (work entries and their comments), `money` (revenue, expenses, fee, markup, finalising), `billing` (billing settings, exchange rates, invoices, payments), `projects` (projects, progress, requirements, meetings, clients), `people` (people, pay changes, permissions, teams) and `company` (company-level events with no domain).
- **Who sees what:** work events only with `work_entry:view_all`; `amount` and `prevAmount` only with `financial:view` (payloads never hold money; the markup in `material_markup_set`'s payload also needs `financial:view`); company-level events (overhead, working time, schedules, company settings, exchange rates) only with `company:view`. The other domain's events never appear.
- Each item has the actor, project and client by name, the subject, the payload as recorded, and for work events the entry as it is now (`null` once deleted). `clientId` matches the event's client or its project's client; `userId` matches who did it.
