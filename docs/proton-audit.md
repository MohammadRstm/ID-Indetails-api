# Proton audit (9 November 2026)

A read-only look at what Proton, the assistant in Hourline, can do today, checked against the code on `master`
(`4ff4092c`) and the client on `main`. Nothing was built, run or changed. `file:line` references are to the API
(`ID-Indetails.api/…`) unless they start with `client:` (`Ident-Internal-Management-System-client/src/…`).

> **Status:** the three high problems (H1–H3) are fixed in PR #193. Everything else below is as found.

## Summary

- **Reads:** 77 read tools, each standing in for a GET endpoint and inheriting its permission, so a person is
  offered only what they could open in the app. 28 of them are always sent; the rest are found by tool search when a
  person has more than 55.
- **Changes:** 103 `propose_…` tools cover 168 of the API's 244 write endpoints. Each one only prepares a card;
  nothing changes until the person presses Confirm. Only the Core family (everyday changes) is on by default; the
  other eight families are off until switched on in `Proton:Actions`.
- **Not covered:** 14 endpoints are planned but not built (site staff, daily logs, pocket money, supervision taken,
  invoice emails, plans and schedules, a payment's account label, a Design VO's TVA), and 18 only open the place in
  the app (uploads, passwords and sign-in addresses, pay, and deleting a project).
- **The newest features:** permit lines and optional site staff reached Proton. Settling what a client owes on
  completion, deleting an archived project and a Design VO's TVA did not: Proton doesn't know them, and its docs
  don't say they are left out.
- **Most important problems (§5):**
  - completing a project through Proton hides what the client still owes and can't settle it (H1);
  - the "hold logging" card says the opposite of what the setting does (H2);
  - the departments tool hands the model each person's labour cost, which Proton's own rules forbid (H3);
  - most "After you confirm, I'll open …" promises open nothing (M1);
  - the docs' counts and lists are out of date (M3).

---

## 1. What Proton can read and answer

Each tool inherits the permission of the endpoint it stands in for (`ProtonEndpoint.Of`, `Proton/Tools/ProtonEndpoint.cs:15-114`)
and calls the same service, so domain separation and money hiding are the API's own. **Default holders** say which
roles hold that permission by default (`Constants/PermissionCatalog.cs:100-155`): **S** super admins, **A** admins in
their own business, **E** employees, **W** Website staff. Everyone needs `proton:use` too. ★ = always sent to the model
(the core set); *heavy* = loads every project's money, at most 4 a question.

### Projects, work and people

| Tool | Endpoint, permission | Default holders | Returns |
|---|---|---|---|
| `find` ★ | project, client, employee and website-project lists | each type by its own list permission | ids by name; archived projects too |
| `list_projects` ★ | `GET /projects`, `project:view` | S A E | filtered list, overdue flag; hours and money only where the service gives them |
| `get_project` ★ | `GET /projects/{id}`, `project:view` | S A E | details, departments' names, a site's staff; no money |
| `get_project_financials` ★ | the money part of `GET /projects/{id}` | needs `financial:view`: S | agreed revenue net and with TVA, billing, cost, profit, permits, supervision, pocket moves, cash profit. **Not** the new settled-owed amount |
| `get_project_requirements` | `requirement:view` | S A E | requirements |
| `get_project_team` | `GET /projects/{id}` (+ `work_entry:view_all` for Design hours) | S A (E for a site's staff) | Design: who logged hours; site: its staff |
| `get_project_departments` | `GET …/departments`, `project:view`; money with `financial:view` | S A E (money: S) | departments, shares, budgets, cost, minutes, **each person's minutes and cost** (see H3) |
| `list_design_vos` | `GET …/design-vos`, `financial:view` | S | VOs, lines, TVA amounts, hours outside scope |
| `list_work_entries` ★, `get_work_entry` | `work_entry:view_all` (or own) | S A; E own | entries with descriptions and comments (Design) |
| `list_my_work`, `list_my_deleted_work` | `work_entry:view_own` | E | the person's own entries |
| `get_time_summary` ★, `get_work_overview` | `report:view` | S A | hours by project, person, client; the Design overview |
| `get_person` ★ | `employee:view` | S A | a person, no pay |
| `get_person_work` ★, `list_people_activity` | `work_entry:view_all` | S A | a person's hours; who is logging (Design) |
| `list_teams` | rule-based | everyone | departments |
| `get_permissions` | `permission:manage` | S | someone's permissions and what their role gives |
| `list_hour_excuses` | `salary:view` | S | a month's excuses, paid or not, never an amount |
| `list_meetings` | `project:view` | S A E | meetings, 92 days at most |
| `get_recent_activity` ★, `summarize_changes` ★ | `activity:view` | S A | the feed; counts of what changed (pay events left out) |
| `get_domain_dashboard` ★ | `report:view` | S A | a business's dashboard and deadlines |
| `list_clients` ★, `get_client` ★ | `client:view` | S A | clients; money only where the service gives it |

### Execution sites

| Tool | Endpoint, permission | Default holders | Returns |
|---|---|---|---|
| `get_site_summary` | `project:view`; cost with `financial:view` | S A E | counts of site expenses and labour weeks |
| `list_site_expenses` | `project:view` | S A E | expenses with costs, currency, category, account label |
| `get_labour` | `project:view` | S A E | weeks, crew defaults and day rates (a site cost, decision 13) |
| `list_boqs`, `get_boq` | `boq:create`, `financial:view` or `project:pricing_manage` | S | BOQs and VOs, work and permit lines, supervision, TVA, what the client pays |
| `list_site_logs` | `site_log:view_all` | S A | everyone's daily logs |
| `list_my_site_logs`, `list_my_sites` | `site_log:create` | E (site staff) | own logs; sites they are staff on |
| `get_site_account` | `financial:view` | S | the site's account: work, permits, supervision, TVA, received, owed, costs, profit, supervision taken, pocket, by label and payee |
| `get_pocket_money` | `financial:view` | S | balance, requests, movements |

### Money and the company

| Tool | Endpoint, permission | Default holders | Returns |
|---|---|---|---|
| `get_domain_finance` | `financial:view` | S | a period's cash (net of TVA), billing today (gross), agreed all time |
| `get_money_by_month` ★ *heavy*, `get_finance_projects`, `get_money_attention` | `financial:view` | S | money month by month; projects ranked; money needing attention |
| `list_invoices` ★, `list_payments` ★, `get_invoice` | `financial:view` | S | invoices and payments, one invoice in full |
| `list_expenses` | `expense:manage` or `financial:view` | S | a Design project's expenses |
| `list_overhead` | `company:view` or `overhead:manage` | S | the company's recurring expenses |
| `get_company_overview` | `company:view` | S | received, project cost, cash profit, overhead; no admin salaries or net profit |

### Messages, files, portal, website, settings

| Tool | Endpoint, permission | Default holders | Returns |
|---|---|---|---|
| `find_recipient`, `get_my_inbox`, `get_my_notifications` | rule-based | everyone | contacts and chats; who wrote and unread counts, **never message bodies**; notifications |
| `get_project_files`, `read_project_file` *heavy* | the files tab's endpoint | S A E | a Design project's files; one file's content for the model (off until the company allows it; 2 a question) |
| `get_file_comments` *heavy* | `project:view` | S A E | the team's comments on a file |
| `get_portal_status` | the portal content endpoint | S A | what the client sees, what the draft changes, who has access |
| `read_discussion` *heavy* | rule-based: those taking part | participants | the discussion with the client |
| `get_website_projects`, `get_website_content` | `website:view` or `website:manage` | S W | website projects and content |
| `get_website_analytics` *heavy* | `website_analytics:view` | S W | visitors |
| `get_company_settings`, `get_domain_settings`, `get_my_board_layout`, `list_my_devices`, `get_my_notification_settings` | rule-based | everyone | settings everyone may read; the person's own layout, phones and push settings |

### Analysis

| Tool | Reads through | Default holders |
|---|---|---|
| `compare_work` ★ | the work report (`report:view`), Design | S A |
| `compare_money` ★ *heavy* | Financials (`financial:view`) | S |
| `review_project` ★ *heavy*, `get_company_summary` *heavy*, `get_attention` ★ *heavy* | projects (`project:view`); each part only with its own endpoint's permission | S A E (parts vary) |

Every analytical result says which days it covers, what it was compared with, and what was left out because the
person can't see it. Judgements ("notable", "unusual") need a rule in `Business/Proton/ProtonSignals.cs` to fire.

### Moving around and showing things

| Tool | What it does |
|---|---|
| `open_record` ★, `open_page` ★, `open_surface` ★ | open a record (and a tab or part), one of 35 pages, or one of 30 forms and windows; each only if the person may open it |
| `fill_form` | fills one of 30 forms for the person to check and save; money and pay fields only from amounts they typed |
| `offer_choices` ★, `suggest_replies` ★ | buttons for an ambiguous name or a few fixed answers; up to 3 suggested replies. None next to a pending card |
| `set_preference`, `refresh_page` ★ | theme and message sounds; reload the page's data |
| `present_data` ★, `add_up` ★ | a chart, table or headline figures from a tool's dataset; totals a column. Read nothing |

These tools are gated by `project:view` or `GET /projects/{id}`, so someone without `project:view` (a Website
admin given `proton:use`) wouldn't get them (question Q3).

## 2. What Proton can change

How a change works:

1. The model calls a `propose_…` tool. It runs inside the read-only guard, reads the target, checks the business
   rules and validates the exact request the endpoint would take (`Proton/Tools/ProtonToolbox.cs:122-219`).
2. The card is stored in `proton_actions` as pending, for 10 minutes, at most 3 per conversation
   (`ProtonToolbox.cs:251-301`).
3. The person presses Confirm. The server checks it is theirs and still pending, the family is still on, the hourly
   cap (30), the daily cap on emails outside the company (20), the typed phrase it stored itself, and that their
   permissions haven't changed since the card. Then it claims the card once, checks the endpoint's permission again
   and calls the same service the endpoint calls (`Proton/Actions/ProtonActionService.cs:36-122`).

**Confirmation strength.** Standard is one click. Strong (T2) is at least two steps (Review, then Confirm), and
often a typed phrase: the record's name, an amount, an invoice number or an email address. Any T2 card a tool draws
as one click is raised to two steps (`ProtonActionTool.cs:108-112`). The two steps are the client's; the server
enforces only the typed phrase.

**Families and the typed turn.** Core and Lifecycle can be proposed from a tapped button or a voice question.
Every other family (Deletes, Access, CompanyRules, PayRules, Money, Messaging, Website) only from a question the
person typed (`ProtonToolbox.cs:131-139`). In production every family but Core is off unless its
`PROTON_ACTIONS_<FAMILY>` is set (`deploy/docker-compose.prod.yml:105-112`).

**Money.** Every amount on a money card must be one the person typed in this conversation (`MoneyText.RequireTyped`);
a payment of an invoice's whole balance uses `full_balance` and the server sets it.

Permissions below are the endpoint's own (`[HasPermission]` on the controller), checked when proposing and again at
Confirm. "Rule-based" means the endpoint has no permission of its own and its service decides (`proton:use` stands
in, `Proton/Tools/ProtonEndpoint.cs:27-58`).

**Confirm** column: *1 click* (Core, Standard), *2 steps* (Review then Confirm), or *types X* (a typed phrase on top).

### Projects, requirements, meetings, clients

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_create_project` | a Design project (client or internal) or an Execution site, staff optional; no money | `project:create` | Core | 1 click; opens the project |
| `propose_update_project` | title, description, location, target date, owner | `project:edit` | Core | 1 click |
| `propose_change_project_status` | hold or resume | `project:change_status` | Core | 1 click |
| `propose_project_lifecycle` | complete, archive, reopen | `project:change_status` | Lifecycle | types the project's name. **No owed settlement or paid-ahead** (H1) |
| `propose_project_billing` | a Design project's client, or client work vs internal | `project:edit` + `financial:view` | Money | types the project's name |
| `propose_project_money` | correct a Design client project's payment, with a reason | `project:edit` + `financial:write` | Money | types the new amount |
| `propose_add_requirement`, `propose_update_requirement`, `propose_set_requirement_done` | add, edit, tick or reopen | `requirement:manage` (ticking also `requirement:complete_own`) | Core | 1 click |
| `propose_schedule_meeting`, `propose_update_meeting` | a project meeting; move or rename one | `meeting:create` or `meeting:manage` | Core | 1 click |
| `propose_create_general_meeting` | a meeting on no project | `meeting:manage` | Core | 1 click |
| `propose_create_client`, `propose_update_client` | add or edit a client and its contacts | `client:create`, `client:manage` | Core | 1 click |
| `propose_set_client_archived` | archive or restore (archiving ends every contact's portal access) | `client:manage` | Core | 1 click (M7) |
| `propose_delete` | delete a requirement, meeting, unused client or Design file; **no projects** | `requirement:manage` always, plus the kind's own (M6) | Deletes | types the project's or client's name (a general meeting: its topic) |

### Design work, departments and VOs

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_log_work`, `propose_update_my_work`, `propose_delete_my_work`, `propose_restore_my_work` | the person's own work (employees) | `work_entry:create` | Core | 1 click |
| `propose_set_work_entry_scope` | mark an entry outside the original scope | `work_entry:view_all` | Core | 1 click |
| `propose_comment_work_entry` | comment on an entry; optionally share to the department chat | `work_entry:comment` | Core (sharing needs Messaging on) | 1 click; types the department's name when shared |
| `propose_project_departments` | which departments work on a project (shares go back to equal) | `project:edit` | Core | 1 click |
| `propose_department_shares` | the shares, in the person's percentages | `financial:write` | Money | types the project's name |
| `propose_design_vo` | make, apply or cancel a Design VO; always with TVA (M2) | `financial:view` + `financial:write` | Money | types the project's name |
| `propose_send_design_vo` | email a VO to the client | `financial:write` | Money, ExternalSend | types the address |
| `propose_blocked_change` | force through or drop what the profit floor refused | `financial:view` | Money | force: types the project's name; drop: 2 steps |

### Execution sites

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_boq` | make, copy lines, duplicate or delete a BOQ or VO | `boq:create` or `project:pricing_manage` | Money | delete: types its title; others 2 steps |
| `propose_edit_boq` | change lines (work or permit), rates, adjustment | the same | Money | types the new work + permits total (client BOQ or VO) |
| `propose_boq_agreement` | agree, withdraw, record a delivery | `project:pricing_manage` | Money | agree and withdraw: types what the client is charged |
| `propose_send_boq` | email a BOQ to the client | `project:pricing_manage` | Money, ExternalSend | types the address |
| `propose_site_pricing` | supervision rate and TVA | `project:pricing_manage` | Money | types the project's name; rates must be typed |
| `propose_add_site_expenses`, `propose_site_expense_step` | add up to 20 costs (USD or LBP), correct or delete one | `material:create` / `material:manage` | Money | add: types the total; delete: types its cost; correct: 2 steps |
| `propose_submit_labour_week`, `propose_labour_defaults` | a week of ground labour; the crew's defaults | `labour_report:create` / `labour_report:manage` | Money | week: types its total; defaults: 2 steps |

### Billing and company money

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_create_invoice`, `propose_update_invoice` | a draft invoice; change a draft | `invoice:create` | Money | 2 steps |
| `propose_invoice_step` | issue, void, discard, unschedule | `invoice:send` / `invoice:void` / `invoice:create` | Money | issue and discard: types the total; void: types the number; unschedule: 2 steps |
| `propose_record_invoice_delivery` | note an invoice was delivered by WhatsApp or by hand | `invoice:send` | Money | 2 steps |
| `propose_record_payment` | a client payment (or a site's initial one), under an account label | `payment:record` | Money | types the amount (or `full_balance`) |
| `propose_payment_step` | apply a payment to an invoice, or void it | `payment:record` / `payment:void` | Money | void: types the amount; apply: 2 steps |
| `propose_add_expense`, `propose_delete_expense` | a Design project's expenses | `expense:manage` | Money | types the amount |
| `propose_billing_settings` | invoice prefix, terms, footer (never bank details) | `billing_settings:manage` | Money | 2 steps |
| `propose_exchange_rate` | a dated rate | `exchange_rate:manage` | Money | types the rate |
| `propose_overhead` | the nine company-expense endpoints | `overhead:manage` (categories `overhead_category:manage`) | Money | amounts typed; cancel and remove: types the name; others 2 steps |
| `propose_proton_top_up` | top up Proton's monthly budget | `proton_budget:manage` | Money | types the amount |

### People and access

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_update_employee` | name, phone, position, department | `employee:manage` | Access | 2 steps |
| `propose_employee_access` | role, move to the other business, remove photo | `employee:manage` | Access | types the person's name |
| `propose_deactivate_employee`, `propose_reactivate_employee` | never oneself | `employee:manage` | Access | deactivate: types the name; reactivate: 2 steps |
| `propose_end_sessions` | end someone's sessions or restore their sign-in | `session:manage` | Access | 2 steps |
| `propose_permissions` | grant, revoke or reset up to 20, with a reason | `permission:manage` | Access | types the name |
| `propose_invitation` | invite or re-invite an employee (never an admin) | `employee:invite` | Access, ExternalSend | types the address |
| `propose_create_team`, `propose_rename_team` | departments | `team:manage` | Core | 1 click |
| `propose_delete_team` | an empty department | `team:manage` | Deletes | types its name |

### Pay rules and company settings

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_working_time` | the company week, break, overtime multiplier | `working_time:manage` | PayRules | 2 steps |
| `propose_schedule` | someone's own week | `schedule:manage` | PayRules | 2 steps |
| `propose_hour_excuse` | add or remove excused hours | `hours:excuse` | PayRules | add: 2 steps; remove: types the name |
| `propose_company_settings` | name, address, week start, currency… | `company_settings:manage` | CompanyRules | types the company name for week start or currency; else 2 steps |
| `propose_domain_settings` | a business's logging rules and notifications | `domain_settings:manage` | CompanyRules | types the business's name when hold logging changes (H2) |
| `propose_company_proton` | let Proton read files; reply suggestions for everyone | `company_settings:manage` | CompanyRules | types the company name |
| `propose_company_contact_email`, `propose_remove_company_logo` | | `company_settings:manage` (or `website:manage`) | CompanyRules | types the company name |
| `propose_account_label` | create, rename, archive, restore, order account labels | `company_settings:manage` | CompanyRules | rename and archive: types the label; others 2 steps |

### Messages, notifications, the person's own settings

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_send_message` | one message as the person, to someone they named | rule-based | Messaging | 2 steps; types the recipient's name with figures, mentions or a big team; 20 a day |
| `propose_send_announcement` | write in Announcements | `message:announce` | Messaging | types "announce" |
| `propose_report_bug` | email a bug report to the developers | rule-based | Messaging, ExternalSend | 2 steps; types "report" if it has figures |
| `propose_mark_chat_read`, `propose_mark_notifications_read` | | rule-based | Core | 1 click |
| `propose_update_my_profile`, `propose_arrange_board`, `propose_my_proton_suggestions`, `propose_notification_settings` | own name and phone, card order, Proton switch, push settings | rule-based | Core | 1 click |
| `propose_remove_my_photo`, `propose_remove_device` | own photo; sign out one of one's phones | rule-based | Deletes, Access | 2 steps |

### Files, client portal and discussion (Design client projects)

| Tool | What | Permission | Family | Confirm |
|---|---|---|---|---|
| `propose_rename_file`, `propose_restore_file`, `propose_reorder_files`, `propose_file_for_client` | | `project:view` (the service checks who may manage files) | Core | 1 click |
| `propose_comment_on_file`, `propose_edit_file_comment`, `propose_set_comment_fixed` | the team's comments (clients never see them) | `project:view` | Core | 1 click |
| `propose_delete_file_comment` | | `project:view` | Deletes | types the project's name |
| `propose_portal_invitation`, `propose_portal_end_access` | invite a client's contact, or end their access | `client_access:manage` | Access (invite is ExternalSend) | types the contact's name or address |
| `propose_portal_heroes`, `propose_portal_summary` | the draft's main pictures and introduction | `client_portal:manage` or `client_access:manage` | Access | 2 steps |
| `propose_portal_step` | publish the draft (the client sees it) or discard it | the same | Access | types the project's name |
| `propose_discussion_post` | a message or file comment to the client | rule-based (participants) | Messaging | types the project's name |
| `propose_discussion_change` | edit or delete one's own message or comment | rule-based | Messaging | delete: types the name; edit: 2 steps (types it if figures) |
| `propose_mark_discussion_read` | | rule-based | Core | 1 click |

### The public website (Website family, off by default)

| Tool | What | Permission | Confirm |
|---|---|---|---|
| `propose_website_project` | add, change, delete a website project | `website:manage` | types its title |
| `propose_website_service`, `propose_website_location` | services and offices | `website:manage` | types the title or city |
| `propose_website_social_link`, `propose_website_order` | social links; the order of services, offices, links | `website:manage` | types the label or "… order" |
| `propose_website_picture`, `propose_website_conveyor`, `propose_remove_website_favicon` | pictures' details and order, the moving strip, the favicon | `website:manage` | types the project's title, "moving strip", "remove favicon" |
| `propose_website_under_construction` | take the site down or bring it back | `website:manage` | types "under construction" or "website live" |

## 3. What it can't do yet

### The 18 `Open` entries: Proton only opens the place

| Endpoint | Why Proton doesn't do it |
|---|---|
| `Projects.Delete` | New: deleting an archived project made by mistake. "Can't be undone and needs the project's name typed in the app": Proton opens the project (`ProtonCoverage.cs:183`) |
| `Files.Upload`, `ProjectFiles.StartUpload`, `.PartLinks`, `.Complete`, `.Cancel` | a file from the person's device |
| `Company.SetLogo`, `Employees.SetPhoto`, `Profile.SetPhoto` | a file from the person's device |
| `Website.AddImage`, `Website.SetFavicon`, `Website.CheckWatermark` | a file from the person's device |
| `Auth.ChangePassword`, `Auth.ChangeEmail`, `MobileAuth.ChangePassword`, `Employees.ChangeEmail` | passwords and sign-in addresses never go through a chat |
| `Employees.ChangePay` | pay amounts never reach Proton |
| `Auth.Logout` | signing out ends the session Proton runs in |

### The 14 `Planned` entries: classified, not built

| Endpoint(s) | Tier, family | Note |
|---|---|---|
| `Projects.UpdateStaff` | T1 Core | putting site staff on or taking them off. Since a site can now start with no staff, this is the only way to add them later, and Proton can't |
| `SiteLogs.Create`, `SiteLogs.Update` | T1 Core | site staff writing or editing their daily log. Proton reads logs (`list_site_logs`, `list_my_site_logs`) but can't write one |
| `Pocket.Contribute`, `.Ask`, `.Approve`, `.Deny` | T2 Money | Proton reads the pocket (`get_pocket_money`) and opens its windows (`to_pocket`, `ask_pocket`) |
| `SupervisionWithdrawals.Create`, `.Delete` | T2 Money | supervision taken; read through `get_site_account` |
| `Invoices.Email`, `Invoices.CreatePlan`, `Invoices.Schedule` | T2 Money, ExternalSend | emailing an invoice, payment plans and scheduled sends |
| `Payments.SetLabel` | T2 Money | a payment's account label (its money is never edited, only its label) |
| `DesignVos.SetTva` | T2 Money | New (5 Nov): taking a Design VO's TVA off or putting it back |

The other kinds: 31 `Elsewhere` (someone else's sign-in, invitation or portal side, the phone's own device, a website
visitor), 2 `ReadOnly` POSTs and 11 of Proton's own controls (`ProtonCoverage.cs:253-381`). The counts are pinned in
`ID-Indetails.UnitTests/Proton/ProtonCoverageTests.cs:56-68`.

### The newest features

| Feature | In the API | In Proton | Words and prompts |
|---|---|---|---|
| **Settling what a client owes on completion** (`settleOwed`: excused or written off; also the older `keepPaidAhead`) | `POST /api/projects/{id}/complete` with `CompleteProjectRequest` (`Controllers/ProjectsController.cs:111-114`) | **Not known.** `propose_project_lifecycle` calls `ChangeAsync(projectId, action)` with neither (`Proton/Actions/LifecycleActions.cs:95`); its input has no field for them | Nothing: the tool, `actions.md:22` and `docs/proton.md` don't mention it, and the card doesn't show what is still owed or paid ahead |
| **Deleting an archived project made by mistake** | `DELETE /api/projects/{id}` and the `GET …/deletion` preview (`ProjectsController.cs:116-126`), `project:delete` | `Open` only: Proton opens the project | Not in any prompt or in `docs/proton.md`. `actions.md:40` lists what Proton can delete, without projects; `navigation.md:11` says delete dialogs can't be opened. No read tool for the deletion preview |
| **Permit lines on BOQs** (no supervision, no TVA) | `BoqLineNature`, `BoqRules.TotalsOf` | **Known.** `propose_boq`, `propose_edit_boq`, `propose_boq_agreement` show permits apart (commits `4ffeb957`, `951bb6f6`) | Up to date: `money.md:12-13, 36, 41`, `actions.md:35-36`. Duplicate and delete cards show the work-only total (`BoqActions.cs:123, 147, 197`) |
| **Optional site staff** | a site can be created with no staff (`6406ceb6`) | **Known** for creating (`ProjectActions.cs`, commit `24301448`). Adding or removing staff later is `Planned` | `actions.md:8-9` still reads "an Execution site … with the staff who oversee it", as if required |
| **Design departments** | `PUT …/departments`, `…/departments/shares` | **Known:** `propose_project_departments` (Core), `propose_department_shares` (Money), `get_project_departments`, `open_surface project_departments` | Up to date; `proton.md:36-37` explains that the data still says "team" |
| **Design VOs** | create, confirm, cancel, email, and the new TVA switch | Create, apply, cancel and email are known (`propose_design_vo`, `propose_send_design_vo`, `list_design_vos`). The TVA switch is not: Proton never sets `ChargeTva`, so every VO it makes is taxed (`DesignVoActions.cs:130-133`) | Nothing says a VO's TVA can't be waived through Proton |

`CLAUDE.md` says a new or changed feature needs its Proton read tool, a way to open it, its wording in the prompts,
"or a note in docs/proton.md of what is left out and why". The last four commits added coverage entries only, with
no note.

### Other gaps noticed

- **Proton can't write anything for a client's eyes it didn't get from the person,** but nothing checks that: a
  discussion post or portal introduction is described as "in the user's own words", yet the text isn't compared with
  what they typed (`Proton/Actions/PortalDiscussionActions.cs:38-116`). The guard is the card itself, the typed project
  name and a warning when the text has figures.
- **No read tool** for blocked changes beyond their notification, or a site's money week by week (`proton.md:38-39`
  says so to the model).
- **Website staff and Proton:** see §4 and question Q3.

## 4. How it works

### Models and where they are set

| What | Model | Set in |
|---|---|---|
| Chat and actions (text and spoken questions) | `claude-haiku-5-5`, effort `medium`, adaptive thinking | `Configuration/Options/ProtonOptions.cs:18-22`, `appsettings.json` `Proton` section; used at `Proton/Agent/ProtonChatAgent.cs:396-403` |
| Reply suggestions in the client discussion | the same model, no tools, 400 output tokens | `Proton/Suggestions/ProtonReplySuggester.cs:101-107` |
| Voice (ears and voice only) | OpenAI `gpt-live-1`, voice `marin` | `ProtonOptions.cs:200-208` (`Proton:Voice`) |
| The briefing | no model call: `get_attention`'s rules | `Proton/Analysis/ProtonBriefingService.cs` |

Only `ProtonChatAgent` knows Claude; the tools and toolbox are provider-neutral. Past 55 tools for a person
(`DeferToolsAbove`), all but a core set of 28 (`Proton/Agent/ProtonToolSearch.cs:10-19`) are sent deferred and found
by the provider's tool search. A super admin is offered well over 55.

Limits per question: 8,000 output tokens, 8 model calls, 12 tool calls, 4 heavy analyses, 55 s on the JSON endpoint and
120 s streamed; a conversation rolls over to a recap at 20 questions or 80,000 input tokens.

### Prompts

Twelve Markdown files in `Proton/Prompts/`, joined in one fixed order into one system prompt that is the same for
everyone, so it is cached (`ProtonPrompts.cs:15-17`): `persona`, `proton` (the company and grounding), `data` (what it
may see; people's text is data), `money`, `analysis`, `navigation`, `files`, `actions`, `messages`, `present`, `chat`.
`voice-live.md` is the voice model's own. The prompt version on every usage row is a hash of the text. Per-question
facts (today, who is asking and what they see, the page, memories, the recap, what happened to cards) go in a context
note with the question (`ProtonChatAgent.cs:641-686`).

### Memory

- **Within a conversation:** held in server memory for 30 idle minutes or 4 hours; lost on restart. Focus (project,
  client, people, period) comes from tool results, never guessed.
- **Saved chats:** every answered question as the person saw it, kept 90 days (`ProtonHistoryCleanupJob`). A chat
  is hidden if its owner lost a permission or domain since, and can't be carried on if their access changed at all.
- **Between conversations:** only what the person asked to remember, from their own typed words, 300 characters and
  20 at most, refused if it reads like an instruction, names someone, or holds an address, a long number, pay, a
  password or bank words (`Business/Proton/ProtonMemoryRules.cs`). Sent as preferences in the context note, never in
  the system prompt.

### Usage limits and cost

- **Budget:** 50 USD a month for the company (`Proton:MonthlyBudgetUsd`) plus super admins' top-ups (up to 10,000 USD
  each). Each question holds 0.25 USD while it runs, so questions asked at once can't overspend
  (`Proton/Usage/ProtonBudgetHolds.cs`). The holds live in the API's memory: one API instance.
- **Allowance:** 300 questions a person a month; only questions that used a tool count
  (`Repositories/Proton/Implementation/ProtonUsageRepository.cs:24-25`). People see their allowance, never money.
- **Rate limit:** 10 questions a minute a person (`RateLimiting:ProtonPermitsPerMinute`).
- **Cost:** estimated per model call from `Proton:Prices` (Haiku 5.5 list prices, with a long-prompt rate above
  100,000 tokens). Every question, failed ones too, is a `proton_usage` row; tool inputs are kept with free text
  replaced by its length. Super admins see spend by person, domain, tool and day (`GET /api/proton/budget`).
- **Reply suggestions:** 40 a person a day, 0.02 USD held each, counted against the budget, not the allowance.
- **Voice:** 0.05 USD a minute estimated, 10 minutes a session, one session a person, closed by the server when the
  budget runs out.

### Voice

Off by default (`Proton:Voice:Enabled`, needs its own OpenAI key). The browser sends its WebRTC offer to the API, which
starts the session with `store: false` and a hashed user id, and watches it to close it on time or budget
(`Proton/Voice/ProtonVoiceProvider.cs:72-86`, `ProtonVoiceWatch.cs`). GPT-Live has no tools; each spoken question is
handed to the same text agent, so it has the same tools, checks and cards. A spoken turn can't propose any gated
family or fill a form that sends something, and speech never confirms (`ProtonToolbox.cs:131-139`).

Worth knowing: the voice provider does receive company data. The session starts with the conversation's earlier
questions and answers as history (`ProtonVoiceProvider.cs:61`), and every answer passes through it to be spoken.
`voice-live.md:3` "no company data" means it has no access of its own.

### Who gets Proton

| Who | Proton? | Why |
|---|---|---|
| Super admins | yes, everything they may see | all permissions (`Constants/PermissionCatalog.cs:148`) |
| Admins of Design or Execution | yes | `proton:use` has no domain, so business admins get it (`PermissionCatalog.cs:151-152`) |
| Employees | yes (since Stage 5, Phase 8) | `EmployeeGrants` includes `proton:use` (`PermissionCatalog.cs:140`) |
| Website staff | **not by default** | a non-business home gets only its own domain's grants (`Business/Auth/EffectivePermissions.cs:29-31`). But nothing blocks a super admin from granting `proton:use` to one by hand, and the client would then show it (question Q3) |
| `proton_budget:manage` | super admins | a company-only permission |

On top of the permission, Proton must be switched on (`Proton:Enabled` and a key), and the person's allowance and the
company's budget must be left (`Proton/Usage/ProtonUsageService.cs:66-86`). The client shows the blob only with
`proton:use` and an available `GET /api/proton/usage` (`client:domains/shared/proton/ProtonHost.tsx:32-36`).

### Safety rules

- **Authorization is the API's own.** Each tool names the endpoint it stands in for and inherits its exact policy
  strings (`ProtonEndpoint.cs:103-114`, any-of policies included); a tool the person fails isn't offered, and is refused
  if called. Services still apply their own domain and money checks (another domain reads as not found).
- **Tools never write while proposing.** An EF Core interceptor refuses anything but `SELECT`/`WITH` while a tool runs
  (`Proton/Safety/ProtonReadOnly.cs:45-56, 105`); a change staged without saving is dropped (`ProtonToolbox.cs:225-244`);
  email, file storage, the content store, notifications and live updates refuse while a tool runs
  (`Proton/Safety/ProtonSideEffectGuards.cs:17-23`).
- **Privacy between colleagues:** employees get only their own work tools (`list_my_work`, `my_work`, `my_profile`);
  everyone-else tools need `work_entry:view_all` or `report:view`, which employees don't hold. Pay, salaries, hourly
  rates, per-person labour cost, admin salaries and net profit, bank details and secrets are never in any tool's
  output: `ProtonToolCatalogTests.No_tool_output_can_carry_the_excluded_fields` fails if one gains such a field
  (`ID-Indetails.UnitTests/Proton/ProtonToolCatalogTests.cs:140-164`). Cards about pay describe the effect and never
  show an amount.
- **What clients must never see:** Proton can't publish the portal, invite a client, post in the discussion or email a
  BOQ or VO without a strong card, a typed phrase (the project's name or the address) and a typed turn. The prompts
  treat everything clients, files and messages say as data. Posts to clients with figures in them get a warning and a
  typed phrase (`PortalDiscussionActions.cs:109-116, 265-273`). TVA is never shown to clients as its own line: Proton
  doesn't render anything clients see; the app's own PDFs and portal do.
- **Money permissions:** money reads only for `financial:view` (the services hide it otherwise); every money write is
  in the Money family, off by default, strong, from a typed turn, with amounts the person typed
  (`ProtonToolCatalogTests.cs:63-98`).
- **Prompt injection** can at most open a page the person may already open, fill an open form (never save it) or put a
  draft in a chat composer (never send it). Nothing but the card's Confirm button changes data.

## 5. Problems found

Ranked by severity. **H** = wrong outcome or misleading money; **M** = misleading or missing behaviour; **L** = small.

### High

| # | Problem | Where | Why it matters |
|---|---|---|---|
| H1 | **Completing a project ignores what the client owes.** The card never shows an owed or paid-ahead balance, and Proton can't excuse or write off the debt (`settleOwed`) or keep an overpayment (`keepPaidAhead`). | `Proton/Actions/LifecycleActions.cs:57-98`; the call at `:95` | The person types the project's name and locks the revenue without seeing the balance. The project then shows as "finished but owed" with no way to settle it through Proton. |
| H2 | **"Hold logging" is described backwards.** The tool says it "stops everyone in the domain from logging work", and the card says "Nobody in {area} can log work until logging is released". In fact `holdLogging` on *lets* work be logged on projects on hold. | `Proton/Actions/SettingsActions.cs:144, 211`; the rule is in `Business/WorkEntries/WorkEntryRules.cs:27-28` and `docs/design.md:28` | Proton tells the user the opposite of what the change does, on a CompanyRules card. |
| H3 | **Per-person labour cost reaches the model.** `get_project_departments` returns each person's minutes and cost, which gives their hourly cost. This breaks the "never per-person labour cost" rule (`data.md`, `docs/proton.md:131`). The catalogue test misses it because the field is a bare `Cost`. | `Proton/Tools/DepartmentTools.cs:7, 63-65`; `ID-Indetails.UnitTests/Proton/ProtonToolCatalogTests.cs:142-146` | Only `financial:view` holders (super admins) get it, and they can see it in the app, but it is sent to the model provider. A smaller form of the same: a one-person project's labour cost ÷ hours (`ProjectTools.cs:484-489`, `FinanceTools.cs:257-266`). |

### Medium

| # | Problem | Where |
|---|---|---|
| M1 | **"After you confirm, I'll open …" usually opens nothing.** The card promises it whenever `open_after` is true (the label defaults to the target's name), but only 10 of the 140 action results return a command to open. The invoice draft promises "the project's invoices" every time and never opens them. | `Proton/Actions/ProtonActionTool.cs:82, 103-106`; `BillingActions.cs:56-58, 99-102`; `ProtonActionService.cs:120` |
| M2 | **Design VO TVA.** Proton never sets `ChargeTva`, so every VO it makes is taxed, and it can't take TVA off (`DesignVos.SetTva` is planned). The "price becomes … with TVA" row uses the project's TVA rate, so applying a VO made without TVA in the app shows a wrong figure. `list_design_vos` doesn't say whether a VO carries TVA. | `DesignVoActions.cs:130-133, 146, 181, 374-379`; `Proton/Tools/DesignVoTools.cs` |
| M3 | **Docs out of date.** `docs/proton.md:59-68` says 237 endpoints (13 planned, 17 open); there are 244 (14, 18). It says nothing about deleting a project, a VO's TVA or settling what is owed. The `get_project_departments` row says "no rates" (`:88`). `get_recent_activity` reads one page, not "3 pages" (`:111`; code: `ActivityTools.cs:112-124`). The tools table lacks `get_file_comments`, `get_my_board_layout` and `list_my_deleted_work`. `docs/status.md` doesn't mention Proton at all. | `docs/proton.md`, `docs/status.md` |
| M4 | **Stale words to the model.** `propose_update_project` says "Revenue, client and pricing are changed in the app", though Proton has cards for all three. `propose_create_project` and `actions.md:8-9` say a site's staff "can come afterwards", but adding staff later is only planned. An old comment says completing "stays in the app". | `ProjectActions.cs:20, 107-108, 213`; `Prompts/actions.md:8-9` |
| M5 | **Client: a card can't be retried after a passing error.** Any error on Confirm turns the card to "failed". A 429, the hourly cap, the daily email cap, a family switched off, or a dropped connection all leave the action pending on the server, so the person must reload to try again. (A wrong typed phrase can't happen: Confirm stays off until it matches.) | `client:domains/shared/proton/ActionCard.tsx:86-89`; `ProtonActionService.cs:47-66` |
| M6 | **`propose_delete` always needs `requirement:manage`**, even to delete a client or a meeting. Someone with `client:manage` or `meeting:create` but not `requirement:manage` (an employee deleting their own meeting) never gets the tool. It fails closed. | `PayRulesAndDeleteActions.cs:320-321` |
| M7 | **Archiving a client is one click** but ends every contact's portal access, which restoring doesn't bring back. The card says so in a note. | `ClientActions.cs:235-262`; `Services/Clients/Implementation/ClientService.cs:264` |
| M8 | **Uneven confirm strength.** `ProtonTier.Strong` says a typed phrase "when it can't be undone, moves money or reaches people", yet these strong cards only take 2 steps: the working week, schedules, adding excused hours, crew defaults, correcting a site expense, editing a client-facing discussion message without figures, and dropping a blocked change (marked irreversible). Applying a Design VO isn't marked irreversible. The 2 steps are the client's alone: the server accepts one POST. | `ProtonCoverage.cs:30`; `PayRulesAndDeleteActions.cs:78-82, 153, 228`; `SiteMoneyActions.cs:393, 620`; `PortalDiscussionActions.cs:272`; `BlockedChangeActions.cs:77-86`; `ProtonActionService.cs:69-73` |
| M9 | **Website staff aren't blocked, only not granted.** Nothing in the API stops a super admin from granting `proton:use` to a Website admin. That person would then miss most UI tools (gated by `project:view`), and the panel's "Go to chat" goes to `/company/assistant`. | `Business/Auth/EffectivePermissions.cs:29-31`; `client:domains/shared/proton/ProtonPanel.tsx:52` |

### Low

| # | Problem | Where |
|---|---|---|
| L1 | The side-effect guards wrap 5 services; the push queue and the SignalR publishers aren't wrapped. A read never pushes, so this is defence in depth only. | `Proton/Safety/ProtonSideEffectGuards.cs:17-23`; `IPushQueue` used by `MessagingService`, `WorkLogNotifier` |
| L2 | Agreeing or withdrawing a BOQ re-checks only that BOQ and the site's work price, not permits or rates changed elsewhere since the card. | `BoqActions.cs:547-551` |
| L3 | BOQ duplicate and delete cards show the work-only total, without permits. | `BoqActions.cs:123, 147, 197` |
| L4 | `list_projects` and `find` include archived projects in "all", while the app's "All" now leaves them out (`ExcludeArchived` is never set). Counts can differ from the page. | `DTO/Request/Projects/ProjectRequests.cs:30`; `Proton/Tools/ProjectTools.cs` |
| L5 | Removing an excused hour also needs `salary:view` (it reads the salary detail). Both are super-admin-only today, so no one is affected yet. | `PayRulesAndDeleteActions.cs:261-263` |
| L6 | A reply suggestion with the budget used up, or a failed model call, returns an error instead of `unavailable`. | `Proton/Suggestions/ProtonReplySuggester.cs:99-107` |
| L7 | Stale comments: "employees don't get Proton yet"; "never money or deletion". | `Constants/PermissionCatalog.cs:96`; `ProtonToolCatalogTests.cs:62` |
| L8 | Client: refs to a website project or a department render as plain text (no `open_website_project` / `open_team` step); the chart schema's ref types lack `website_project`, so such a block would be dropped (latent); `toggleProton` is unused. | `client:…/proton/ProtonProvider.tsx:22`, `protonFigures.ts:13`, `protonStore.ts:83` |
| L9 | Missing tests: 7 actions have no Proton integration test (`propose_update_requirement`, `propose_set_requirement_done`, `propose_update_invoice`, `propose_record_invoice_delivery`, `propose_labour_defaults`, `propose_remove_company_logo`, `propose_mark_discussion_read`); no test for H2's wording, H3's field, or that a Website admin gets no blob. | `ID-Indetails.IntegrationTests/Api/Proton*Tests.cs` |

Checked and fine: every write endpoint has a coverage entry; every action re-checks its permission at Confirm; the
typed phrase is the server's own; money amounts must be typed; no read tool returns pay, salaries, bank details,
admin salaries, net profit or secrets; employees can't reach colleagues' hours or pay; prompt injection can't confirm
anything. The eval suite (`ProtonEvalTests`) skips itself without `PROTON_EVAL_API_KEY`.

## 6. Questions for you

| # | Question |
|---|---|
| Q1 | **Owed on completion.** Should Proton offer to excuse or write off what a client still owes, and to keep a paid-ahead amount? That would be a Money card with the amount typed, needing `financial:write` (Design) or `project:pricing_manage` (a site). Or should the completion card only show the balance and send the person to the app? |
| Q2 | **Deleting a project.** Keep it as "open the project", or make it a Deletes card that reads the deletion preview first and asks for the name typed? Either way, should the prompt tell the model the rules? |
| Q3 | **Website staff.** Is "no Proton for Website staff" a hard rule, enforced by the API, or only a default that a super admin may override? If the second, the gaps in M9 need fixing. |
| Q4 | **Per-person cost (H3).** Drop each person's cost from `get_project_departments` and keep their minutes? Or accept it for `financial:view` holders and change the rule? |
| Q5 | **What next from the planned list?** Site staff and daily logs (both Core, useful to site staff), pocket money, supervision taken, invoice emails, plans and schedules, a payment's label, a VO's TVA. |
| Q6 | **Confirm strength (M8).** Should pay-rule cards (working week, schedules, excused hours), crew defaults, a site expense correction and dropping a blocked change ask for a typed phrase? |
| Q7 | **Client archive (M7).** Should archiving a client with portal contacts be a strong card? |
| Q8 | **Production state.** Which `PROTON_ACTIONS_*` families are on in production, and is the `EmployeesUseProton` migration applied? I didn't look at production. |
| Q9 | **Voice and OpenAI.** Voice sends earlier answers (money figures included) to OpenAI as session history, and every spoken answer passes through it. Is that acceptable, or should the history be left out? |
