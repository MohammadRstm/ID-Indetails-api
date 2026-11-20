# Self-service (`/api/me`)

What signed-in people see about themselves. These endpoints only ever return the caller's own data and never take a user id. Hours, overtime and schedule serve Design people for now: they need `work_entry:view_own`, which only people of the Design domain hold. My requirements serves both domains (`requirement:view`). The caller's work entries, summary, projects and day are in [design.md](design.md); their profile is in [employees.md](employees.md), and their profile photo (`PUT` and `DELETE /api/me/photo`) in [files.md](files.md).

**Employees don't see their own pay** (28 Oct 2026): there is no `/api/me/pay`. Their pay is on Salaries, for those with `salary:view` ([company.md](company.md)). They are still told when their pay changes (`pay_changed`), without the amount.

## My requirements

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/requirements` | `requirement:view` | The caller's open requirements, soonest deadline first. In both domains: it is the whole of an Execution employee's My work page |

Rules (`Business/Requirements/MyRequirements.cs`):

- The requirements assigned to the caller on projects they can see that are active or on hold. Completed and archived projects are left out.
- Open ones are listed soonest deadline first; those without a deadline come last, oldest first. Each has `isOverdue` (open with a deadline before today), `daysLeft` (negative once overdue, null without a deadline), its project, and everyone else it is assigned to.
- Counts: `projects` with at least one of them, `overdue`, `dueSoon` (a deadline from today to 7 days ahead) and `done` (already done, not listed).

## My schedule

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/schedule?date=` | `work_entry:view_own` | The caller's week, the company policy in force, and one day's hours (today by default) |
| `POST /api/me/overtime-preview` | `work_entry:view_own` | `{ startedAt, endedAt, entryId, moves }`: the overtime of work about to be logged or edited, among the caller's other work that day (leaving out `entryId`, the entry being edited, and with `moves` applied), and where it falls: before hours, after hours (past midnight included) or past the hours the day asks for, or all of it on a day off. Nothing is saved. The shortfall this overtime first makes up is `monthShortMinutes` above (`Business/Payroll/OvertimePreview.cs`) |

- `schedule`: the caller's week as it stands today, with the same shape as the Schedules screen. It is their own schedule where they have one (`isOwn`, `ownSince`), otherwise the company week. Each working day has its start, end and required minutes, and `nextChangeOn` is a change dated in the future.
- `companyWeek`: the company policy in force today. Its break and overtime multiplier apply to everyone.
- `day`: the day asked for (`date`, today by default, from 2000 to 2100): whether it is a working day, its hours, required minutes and paid break, and what the caller has logged on it. `insideMinutes` is logged time that isn't overtime, and `leftMinutes` is what the day still requires. On a day off everything logged is overtime.
- `day.monthShortMinutes`: what that day's month is short of its required hours so far, from the hours ledger (null where it doesn't apply). Overtime logged in the month first makes this up, as normal hours.
- This is what the log-work form previews against (the prototype's overtime warning): the day's hours and what is already logged, with the entries themselves from `GET /api/me/day?date=`.

## My hours

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/hours?page=1` | `work_entry:view_own` | The caller's hours ledger, month by month, newest first |

- The ledger applies to Design employees only. For anyone else (admins included) `applies` is false and there are no months.
- The months run from this month back to the month the caller joined, 12 a page (`totalMonths` counts them all). Each is built exactly as on the Salaries screen (`HoursLedger`, through `SalaryCalculator.LedgerOf`):
  - `expectedMinutes`: the required hours of the working days so far (up to yesterday while the month runs). `expectedFullMonthMinutes` covers the whole month, from the day the caller joined.
  - `excusedMinutes` and `wholeMonthExcused`: excused time, paid or not.
  - `insideMinutes`: work within the caller's hours.
  - `madeUpMinutes`: overtime that made up missing hours, in date order.
  - `overtimePaidMinutes`: the rest of the overtime.
  - `shortMinutes` and `missedDays`: what is still missing, and the working days with no work within hours and no excuse.
  - `excuses`: the month's excuses, paid or not, with their notes. Who made them isn't shown.
  - `timeOff`: the month's days of approved time off, one item per request (`requestId`, `firstDay`, `lastDay` (the last day taken), `isPaid`, `days`: its working days in the month). They count like excused days: the ledger expects no hours on them ([time-off.md](time-off.md)).
- `status` is `excused` (the whole month), `short` (a closed month still short), `behind` (the month in progress is short so far), `on_track` or `complete` (`Business/Payroll/MyTime.cs`).
- Someone else's ledger, for their profile, is `GET /api/employees/{id}/hours` (`salary:view`; see [design.md](design.md)).

## My overtime

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/overtime?month=YYYY-MM` | `work_entry:view_own` | The caller's overtime in a month (this month by default), day by day |

- Overtime is time outside the caller's hours, all time on a day off, and time within hours beyond what the day requires (taken from the day's latest work).
- The month can't be in the future or before the caller joined (`400`).
- Totals: overtime, how much of it made up missing hours and how much is paid as overtime (both null where the ledger doesn't apply), and the number of entries.
- `days`: days with overtime, newest first, each with whether it was a day off and every entry: project (with its icon and tint), description, start and end, length, overtime and the part that made up missing hours. No money is shown.
- Someone else's overtime, for their profile, is `GET /api/employees/{id}/overtime` (`work_entry:view_all`; see [design.md](design.md)).

## My time off

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/time-off` | `time_off:request` | The caller's own requests, latest first day first |
| `POST /api/me/time-off` | `time_off:request` | `{ firstDay, lastDay, reason }`: asks for time off. Returns `201` |
| `POST /api/me/time-off/{id}/withdraw` | `time_off:request` | Takes back a request that is still pending |

- Design employees only: `time_off:request` is held by employees and never by admins or super admins. Someone else's request reads as `404`.
- A request starts tomorrow at the earliest and at most 6 months ahead, needs a reason, and can't overlap the caller's own pending or approved time off. The whole rules, the response shape and the errors are in [time-off.md](time-off.md).
- The caller sees the paid flag, the reason and the decision and rejection notes, but not who decided (`person`, `decidedByName` and `endedByName` are left out).
- Who is off, for the "Off" flags, is `GET /api/time-off/away`, open to anyone signed in; it never shows the reason or the paid flag.

## My salary advances

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/salary-advances` | Signed in | `{ canAsk, cannotAskBecause, currency, items }`: whether the caller may ask now, why not, the currency of their pay and their own advances, newest first |
| `POST /api/me/salary-advances` | Signed in | `{ amount, reason }`: asks for part of their pay early. Returns `201` |
| `POST /api/me/salary-advances/{id}/withdraw` | Signed in | Takes back a request that is still pending |

- Anyone with pay set can ask: employees, site staff, admins and super admins. An advance is part of their pay handed over early, in the currency of their pay, and comes off what they are handed in the months a super admin chooses. The rules, who decides and what it changes are in [company.md](company.md#salary-advances).
- `cannotAskBecause` is `salary_advance_no_pay` (no pay set today), `salary_advance_pending` (a request is waiting for a decision) or `salary_advance_open` (the last advance isn't paid back yet); `canAsk` is false with any of them, and asking then answers `422` with the same code. `currency` is null with no pay set.
- The form never shows or checks the caller's pay: the cap (a month's pay) is checked when a super admin approves. The amount is above zero and the reason is required (500 characters at most).
- Each item has its `status` (`pending`, `approved`, `declined`, `withdrawn` or `cancelled`), what was asked for (`askedAmount`) and given (`amount`), the `reason`, `decisionNote`, the day it was given, its `months` and what is still to pay back (`left`, null until it is given), and, once settled, how (`settledKind`: `cash` or `written_off`, and `settledOn`).
- Each month in `months` says what it plans to take (`planned`) and whether it has closed. **`taken` is always null in the caller's own list:** in a short month it would show what they were paid. They never see their pay, nor who decided, recorded or settled it.
- Someone else's advance reads as `404`, and withdrawing a request that was already decided is `422 salary_advance_decided`. Their requests and decisions reach them as notifications without amounts ([notifications.md](notifications.md)).

## My board layouts

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/board-layouts/{key}` | Signed in | The caller's saved card order for a board; `order` and `savedAt` are null while nothing is saved (the board shows its default) |
| `PUT /api/me/board-layouts/{key}` | Signed in | `{ order: [cardKey…] }` creates or replaces it |
| `DELETE /api/me/board-layouts/{key}` | Signed in | Goes back to the default order (`204`, also when nothing was saved) |

- The boards are the domain overviews and the company overview: `overview-design`, `overview-execution` and `company-overview` (`Constants/BoardKeys.cs`). Any other key is `404`.
- Only the order is kept (`user_board_layouts.layout` holds `{ "order": [...] }`), as in the prototype: a card takes the width of the slot it lands in. The client drops keys it no longer knows and places new cards at their default spot, so the server only checks the shape: 1 to 30 card keys, each lowercase letters, digits and dashes (40 at most), none twice (`400`).
- The client saves on every move and deletes the layout when the order matches the default again. Nobody reads or changes anyone else's layout.

## My devices

The phones that receive the caller's pushes ([notifications.md](notifications.md#pushes)). Browsers register through `/api/auth/web-push` instead ([notifications.md](notifications.md#browsers-web-push)), and are listed here too, with the platform `web`.

| Method and path | Needs | Purpose |
|---|---|---|
| `PUT /api/me/devices` | The phone app's sign-in | `{ pushToken, platform: ios \| android, appVersion }`: registers this phone for pushes, or updates it |
| `DELETE /api/me/devices/current` | Signed in | Stops pushes to this phone (`204`, also when there is nothing to remove) |
| `GET /api/me/devices` | Signed in | The caller's phones and browsers, newest first: `{ id, platform, appVersion, createdAt, updatedAt, current }` |
| `DELETE /api/me/devices/{id}` | Signed in | Signs that phone out: its session ends and its pushes stop (`204`). Someone else's device reads as `404` |

- **Tied to a session.** A device belongs to the phone app's session it was registered from (the access token's `sid`, see [authentication.md](authentication.md#mobile-sessions)). Registering needs that session: a website token is refused (`403`), and a session that has ended is `401`. `current` marks the caller's own phone.
- **One token, one person.** A push token is unique: registered by someone else (a shared phone), it moves to the caller, so nobody gets the previous person's pushes. A session keeps one device; a new token from the same phone replaces the old one.
- **When devices go.** Signing out on the phone, or on the website, deletes its device. Every other way a session ends (a password change, a super admin, deactivation, theft, 30 idle days) stops its pushes at once, because only devices whose session still has a valid refresh token are listed or sent to, and a daily job deletes them. Expo reporting a token as no longer registered deletes it too.
- The push token is never returned.

## My notification settings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/notification-settings` | Signed in | `{ quietFromMinute, quietToMinute, mutedCategories, isDefault }` |
| `PUT /api/me/notification-settings` | Signed in | `{ quietFromMinute, quietToMinute, mutedCategories }` replaces them and answers as `GET` |

- **Quiet hours** are minutes after midnight in the company's time zone (0 to 1439), both set or both null (no quiet hours), and different from each other (`400` otherwise). A range may cross midnight (1320 to 420 is 22:00 to 07:00). Pushes during them arrive silently.
- **Muted categories** (`work`, `requirements`, `projects`, `meetings`, `sites`, `money`, `people`, `messages`) send no pushes at all; each once (`400`). The in-app list is unchanged.
- **Defaults.** Until the person saves their own, `GET` answers 22:00 to 07:00 with nothing muted and `isDefault: true`.
- Stored in `user_notification_settings` and `user_notification_mutes`. Nobody reads or changes anyone else's.

## Contact the developer

Anyone signed in, staff or a client in the portal, can write to the developer. It used to be "Report a bug"; the route
kept its name.

| Endpoint | Who | What |
|---|---|---|
| `POST /api/bug-reports` | Signed in (staff) | `{ description, details, page, installedApp, error, recentErrors }`: emails the message to `BugReports:ToAddress` and a copy to the sender (`204`). Nothing is stored |
| `POST /api/portal/contact` | Portal token | The same, from a client ([client-portal.md](client-portal.md#endpoints)) |

- `description` (what they were trying to do, and what happened) is required; it and `details` (anything else) take up
  to 4000 characters. `page` is the client path it was sent from and must be a bare path (`/design/projects/12`): a
  query or fragment is refused, so a token in a link can't travel with it.
- `error` is the error they came from, when they followed an error's "Contact the developer" link: `{ ref, at,
  message }`. `ref` is the error response's trace id (or the reference shown to them) and `at` when it happened; a page
  that failed in the browser has no `ref` but the browser's own `message` (one line, at most 300 characters). One of
  the two is needed. `recentErrors` are the requests that failed lately in that browser (at most 5), as before.
- The API adds who sent it (name, user id or portal account id, email, and role and domain, or that it is a client),
  the time, whether it came from the installed app or a browser tab, and the browser (`User-Agent`, one line, at most
  300 characters). The error's time is shown in UTC, as the log has it, and in company time, with the short reference
  to search the log for ([deployment.md](deployment.md#tracing-an-error-back)). Every value is HTML-encoded in the
  email (`Services/Email/Implementation/SupportEmails.cs`).
- **Replies** go to the sender: the email's Reply-To is their address.
- **The sender's copy** has their own words and the error's reference, none of what the API adds. If only the copy
  fails, it is logged and the message still counts as sent.
- **Where it goes:** `BugReports:ToAddress` (`BUG_REPORTS_TO` on the server) is never sent to the client.
  `BugReports:WhatsAppNumber` (`BUG_REPORTS_WHATSAPP`, digits with the country code, default `96171317958`) is
  public: `GET /api/company/branding` returns it as `supportWhatsApp`, so the sign-in pages can show the WhatsApp link.
- Five messages per person per hour, staff or client (`RateLimiting:BugReportPermitsPerHour`, `429` past it). If the
  email can't be sent the answer is `503 email_delivery_failed`, and the failure is logged.
