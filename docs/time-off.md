# Time off

Design employees ask for whole days off ahead of time. Any super admin approves the request, with pay or without, or declines it. While someone is off, no hours land on their days, their department and the super admins hear about it, and the super admins are alerted if the person logs hours or uses the app.

The rules are in `Business/TimeOff/TimeOffRules.cs`. The requests are handled by `Services/TimeOff/Implementation/TimeOffService.cs`, what time off means elsewhere (hours, meetings, alerts, leaving) by `TimeOffWatch.cs`, and the scheduled part by `TimeOffSchedule.cs`. Added on 9 Nov 2026 (migration `TimeOff`).

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/time-off` | `time_off:request` | The caller's own requests, latest first day first |
| `POST /api/me/time-off` | `time_off:request` | `{ firstDay, lastDay, reason }`: asks for time off. Returns `201` |
| `POST /api/me/time-off/{id}/withdraw` | `time_off:request` | Takes back a request that is still pending. Someone else's request is `404` |
| `GET /api/time-off?view=&page=&pageSize=` | `time_off:decide` | The requests to decide on or look back at, with the person (below) |
| `POST /api/time-off/{id}/approve` | `time_off:decide` | `{ isPaid, note? }`: approves a pending request, paid or unpaid |
| `POST /api/time-off/{id}/decline` | `time_off:decide` | `{ note? }`: declines a pending request |
| `POST /api/time-off/{id}/reject` | `time_off:decide` | `{ note? }`: takes approved time off back until its last day |
| `GET /api/time-off/away?from=&to=` | Signed in | Who is off on any day from `from` to `to` (today when both are left out) |

Who holds the permissions: `time_off:request` is limited to Design and held by employees only (it is part of `PermissionCatalog.OwnWork`, so admins and super admins never hold it). `time_off:decide` is a company permission with Pay sensitivity, so only super admins hold it ([authorization.md](authorization.md)). Someone who is not a Design employee gets `403 forbidden` when asking, even if a permission was granted by hand.

## Requests and responses

**Asking:** `firstDay` and `lastDay` are dates (`yyyy-MM-dd`), `reason` is required and at most 500 characters. `note` on a decision is optional and at most 500 characters. The validators check that both days are set, `lastDay` is not before `firstDay`, the request is at most 366 days long (a guard against a typo in the year, not a policy), and the reason is present and not too long (`400 validation_failed`).

**The list** (`view`): `pending` (waiting, soonest first day first), `current` (approved and its last day is today or later, soonest first) or `past` (everything else, latest first day first). Pages are 25 by default, up to 100.

**A request** (`TimeOffResponse`):

| Field | Meaning |
|---|---|
| `id`, `firstDay`, `lastDay`, `reason` | What was asked for |
| `status` | `pending`, `approved`, `declined`, `withdrawn`, `expired`, `cancelled`, `rejected` or `ended` |
| `isPaid` | Null until approved; kept once rejected or ended |
| `workingDays` | The person's working days from the first day to the last |
| `daysTaken` | The working days actually taken: all of them once approved, only those before the rejection day once rejected or ended, none before approval |
| `requestedAt`, `decidedAt`, `decisionNote` | When it was asked, and when and how it was approved, declined, withdrawn or expired |
| `endedOn`, `endedAt`, `endNote` | The rejection day (or the day it was cut short), when, and the note |
| `person` | Who asked: `id`, `name`, `photoFileId`, `departmentName`. Left out of the employee's own list |
| `decidedByName`, `endedByName` | Who approved or declined, and who rejected. Left out of the employee's own list |
| `takenThisYear` | Super admins' list only: the working days of time off the person has taken this calendar year, to weigh the request |
| `meetings` | Super admins' list only, for a pending or approved request not yet over: the meetings the person is already in during it (`id`, `topic`, `startsAt`, `endsAt`). They stay, but the approver is shown them |

**Away** answers `[{ userId, firstDay, lastDay }]` for everyone with granted time off in the range, where `lastDay` is the last day actually taken (earlier than the request's when it was rejected or ended). It has no paid flag and no reason, so the client can show "Off until <date>" to everyone. `to` can't be before `from`, and the range is at most 92 days (`400`). Like `/api/presence`, it is not limited to the caller's domains.

## Rules

**The days.**
- It starts tomorrow at the earliest (`400 time_off_too_soon`) and its first day is at most 6 months ahead (`400 time_off_too_far`). It may span months; there is no yearly allowance.
- It must hold at least one of the person's working days, from their schedule or the company week (`400 time_off_no_working_day`).
- Every calendar day from the first to the last counts as "off": the flag, the hour block, the alerts and the meeting block. Pay and `daysTaken` count only the working days.
- A request can't overlap the person's own pending or approved time off (`422 time_off_overlaps`), nor a day already excused in their hours (a day excuse, or a month excused whole; `422 time_off_excused`). Declined, withdrawn, expired, cancelled, rejected and ended requests don't block a new one.

**Deciding.**
- The first decision wins. Asking, approving, declining, rejecting and withdrawing lock the person's user row first and then the request row (`SELECT … FOR UPDATE`), always in that order. Two super admins deciding at once run one after the other, and the second gets `422 time_off_decided`. The same person lock keeps a request, an approval and an excuse by hand from overlapping.
- Approving needs the request pending (`422 time_off_decided`), its first day after today (`422 time_off_expired`, because on its first day a pending request expires) and no day excused by hand since it was asked (`422 time_off_excused`).
- Declining only needs it pending (`422 time_off_decided` otherwise).
- The employee withdraws only while it is pending (`422 time_off_decided` once decided, expired or cancelled).
- A decision note is optional. The reason is seen only by the employee and the super admins.

**Expiry.** A request still pending when its first day comes expires (status `expired`) at the first scheduled run that day, and the employee is told. The day before, from 09:00, the super admins are reminded of requests still pending.

**Taking it back (rejecting).**
- Only approved time off can be rejected (`422 time_off_not_approved`), on any day up to and including its last day (`422 time_off_over` after).
- The rejection day is stored as `endedOn`. The days before it stay time off, paid or unpaid as approved. The rejection day and every day after are normal work days, with the usual hours required that day.
- Rejected before the first day, no day is taken at all.
- The employee is told (`time_off_rejected`). If it was under way (its first day has come), the department and the super admins hear that the person is back (`time_off_back`).

**The hour block** (`TimeOffWatch.CheckHoursAsync`, called by `WorkEntryService`). Two rules, both `422`:
- No hours can be dated on a day of time off: not a new entry, an edit, a move of another entry, nor a restore (`time_off_day`).
- While the person is off today and changes hours themselves, they can add, edit, move, delete or restore none (`on_time_off`), whatever days the change is about.

When hours are logged for the person rather than by them (`ICurrentUser.IsActing`: the parked hours released when an admin adds a department, and a blocked change a super admin forces through, replayed as the person), only the first rule applies. Those hours still go through for days outside the time off, with no alert.

**Alerts to the super admins.** Each links to the People page filtered to the person (`GET /api/employees?id=`), where their sessions can be ended. Both are saved and sent on their own before the request that caused them fails, so a refused request still alerts. Neither names the reason or whether it is paid.
- `time_off_hours`: the person, off today, tried to log or change hours. At most once an hour per request (key `time-off-hours-{requestId}-{yyyyMMddHH}`, company time).
- `time_off_online`: the person, off today, used the app. At most once a day per request (key `time-off-online-{requestId}-{yyyyMMdd}`). It is sent from `LastSeenMiddleware` when it records "last seen" (at most every 5 minutes per person); a failure there never fails the request.

**Heads-up.** On the first day, at the first scheduled run (from 00:00 company time), `time_off_starts` goes to every active person in the person's department (employees and admins) plus every super admin, never the person themselves. A department with no admin tells nobody extra. It is sent once per request (key `time-off-starts-{requestId}`) and only while the time off is approved and under way. The payload holds the name and last day only. The back notice (`time_off_back`) goes to the same people.

**Meetings.** From approval on, nobody can be added to a meeting on a day of the person's time off, nor have a meeting they attend moved onto one (`422 meeting_attendee_off`). Meetings they were already in stay. The check covers added attendees on an edit, every attendee when the meeting moves, and the attendees of a new meeting. See [design.md](design.md).

**Hand excuses.** A day inside granted time off can't be excused by hand, nor can a month that holds any be excused whole (`422 excuse_on_time_off`): excuse the other days instead. See [company.md](company.md).

**Leaving, moving and role change.** When a Design employee is deactivated, moves out of Design, or becomes an admin or super admin (`EmployeeService`, through `ITimeOffWatch.EndAsync`):
- pending requests are cancelled (status `cancelled`, with a note saying why);
- approved time off from today on ends (status `ended`, `endedOn` is today, so the days already taken stay and today and later are dropped);
- the super admins are told (`time_off_ended`), once for the person, and an activity event `time_off_ended` is written per request.

Nothing happens when they had no open requests. Time off already over is left alone.

## Pay

Granted time off (approved, rejected or ended) feeds pay as **day excuses**, one for each month it touches (`TimeOffRules.ExcusesIn`, used by `PayrollInputs` and the missed-hours check). Every pay, ledger and reminder rule for day excuses then applies unchanged ([company.md](company.md)):
- The hours ledger doesn't expect hours on those days, and a month doesn't read as short because of them.
- Missed-hours and log reminders skip them.
- Paid days count toward the overtime threshold; unpaid days don't.
- On a salary, a paid day changes nothing and an unpaid day deducts the full paid day.
- On an hourly rate, each paid working day pays the person's normal paid day (the day's hours plus its paid break, `Span × rate`). An unpaid day pays nothing. This is `timeOffPay` in the salary detail, and it is part of the base pay.
- It is a company cost and is never charged to a project.

Differences from an excuse by hand: time off may be for a future month, it has a status, it is decided by request, and a paid day pays hourly staff (an excuse never does).

Salary detail (`GET /api/company/salaries/{userId}`) carries `timeOff` (each request touching the month: `requestId`, `firstDay`, `lastDay`, `isPaid`, `days`: its working days in the month) and `timeOffPay`. `GET /api/me/hours` carries the same `timeOff` list per month. Both are read-only: time off is decided on the Time off page, not on Salaries.

## Privacy

- Only super admins and the employee see whether it is paid, the reason and the time-off activity.
- Everyone else, through the heads-up, the "Off" flag (`/away`) and the activity feed, sees only that the person is off and until when. The feed hides every time-off event from callers without `time_off:decide`.
- No notification payload carries the reason. `isPaid` goes only to the employee (`time_off_approved`).
- Employees never see how many hours their colleagues log or are paid, and time off doesn't change that.

## Notifications and activity

Notification kinds (all in the `people` push category, [notifications.md](notifications.md)):

| Kind | To | When |
|---|---|---|
| `time_off_requested` | Super admins | Someone asked for time off |
| `time_off_withdrawn` | Super admins | Someone took back a pending request |
| `time_off_undecided` | Super admins | Scheduled: a request starting tomorrow is still pending, from 09:00 the day before |
| `time_off_approved` | The employee | Approved (payload has `isPaid`) |
| `time_off_declined` | The employee | Declined |
| `time_off_rejected` | The employee | Approved time off taken back from today |
| `time_off_expired` | The employee | Scheduled: nobody decided before its first day |
| `time_off_starts` | The person's department and the super admins | Scheduled: the first day has come |
| `time_off_back` | The person's department and the super admins | Time off under way was rejected |
| `time_off_ended` | Super admins | Cancelled or cut short: they left, moved out of Design or became an admin |
| `time_off_online` | Super admins | Someone on time off used the app, once a day |
| `time_off_hours` | Super admins | Someone on time off tried to change hours, once an hour |

The six sent to super admins (`requested`, `undecided`, `withdrawn`, `ended`, `online`, `hours`) are only listed and openable while the reader holds `time_off:decide` (`NotificationAccess`).

Activity events (Design domain, subject: the person, payload: the request, its days, its status and whether it is paid): `time_off_requested`, `time_off_withdrawn`, `time_off_approved`, `time_off_declined`, `time_off_expired`, `time_off_rejected`, `time_off_ended`. Only callers with `time_off:decide` see them.

## The scheduled check

`ScheduledCheck.TimeOff` runs with the other scheduled checks (every `NotificationScheduler:IntervalMinutes`, 60 by default; [notifications.md](notifications.md)). Each run:
1. expires pending requests whose first day is today or earlier, and tells the employee (`time-off-expired-{id}`);
2. from 09:00, reminds the super admins of pending requests starting tomorrow (`time-off-undecided-{id}`);
3. sends the heads-up for approved requests under way today (`time-off-starts-{id}`).

Every notification is keyed, so running twice sends once.

## Errors

| Code | Status | When |
|---|---|---|
| `time_off_too_soon` | 400 | The first day is today or earlier |
| `time_off_too_far` | 400 | The first day is more than 6 months ahead |
| `time_off_no_working_day` | 400 | The days hold none of the person's working days |
| `time_off_overlaps` | 422 | Overlaps the person's own pending or approved time off |
| `time_off_excused` | 422 | Some days are excused by hand (when asking, or since, when approving) |
| `time_off_decided` | 422 | Already decided, withdrawn, expired or cancelled |
| `time_off_expired` | 422 | Approving on or after its first day |
| `time_off_not_approved` | 422 | Rejecting something that isn't approved |
| `time_off_over` | 422 | Rejecting after its last day |
| `time_off_day` | 422 | Hours dated on a day of time off |
| `on_time_off` | 422 | The person is off today and tried to change hours |
| `meeting_attendee_off` | 422 | Putting someone in a meeting on a day they are off |
| `excuse_on_time_off` | 422 | Excusing a day of time off by hand, or a month holding some whole |
| `department_required` | 400 | A Design admin or employee given no department ([employees.md](employees.md)) |

## Tables

`time_off_requests`, one row per request ([database.md](database.md)). Its status is a snake_case string with a check; `is_paid` is set exactly when the status is approved, rejected or ended; `ended_on` is set exactly when it is rejected or ended and is never after `last_day`.

## Proton

Not covered yet (the user's decision, 9 Nov 2026). The five write endpoints are `Open` entries in `ProtonCoverage` (Proton points to the page), and Proton has no read tool for time off. Proton is still blocked from logging hours on a day off or during time off, because it goes through the same `WorkEntryService`. See [proton.md](proton.md).
