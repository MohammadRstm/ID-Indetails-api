# Billing

Billing is a cash ledger kept apart from a project's profitability: invoices and payments never change a project's agreed revenue, cost or profit. Payments are the revenue of a period, though: the Financials summary, money by month and the company overview count the money received in the period, so recording or voiding a payment moves that period's revenue and profit. Every amount has its currency beside it and nothing is converted silently. Invoice and payment status and balances are always calculated, never stored.

## Billing settings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/billing-settings/{domain}` | `billing_settings:manage` or `financial:view` | The domain's settings, or `404 billing_settings_missing` before they are set up |
| `PUT /api/billing-settings/{domain}` | `billing_settings:manage` | Creates them the first time, replaces them afterwards |

- Each domain has its own settings: invoice prefix, default payment terms (0–365 days), bank account, bank name, IBAN, SWIFT, terms text and footer, and its overdue reminders (below): `remindersEnabled` (off until turned on), `reminderFirstAfterDays` (1–60, default 1), `reminderEveryDays` (1–60, default 7) and `reminderMax` (1–10, default 3).
- The **invoice prefix** is 1–8 capital letters or digits. It can be changed at any time; invoices already issued keep their number. Invoice numbers are unique across the company, so two domains can't use the same prefix (`409 invoice_prefix_taken`).
- The settings are not seeded. No invoice can be issued in a domain until they exist.
- A domain the caller can't see, or an unknown domain, reads as `404`. Every save writes `billing_settings_updated`.

## Exchange rates

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/exchange-rates?currency=` | `financial:view` | Every dated rate, newest first per currency |
| `POST /api/exchange-rates` | `exchange_rate:manage` (super admins) | `{ currency, ratePerUsd, effectiveFrom }` adds a dated rate |

A rate says how many units of a currency one US dollar buys, from its effective date. The rate in force on a day is the latest one effective on or before it. Rates are company-wide, so only super admins set them.

Rates are append-only, like pay history (`Business/Billing/ExchangeRateRules.cs`): a new rate applies from today or earlier (`400 rate_date_in_future`) and after the currency's latest rate (`422 rate_not_after_latest`). Payments already recorded keep the rate they were recorded with. Adding one writes `exchange_rate_set` with no domain and the rate in the `amount` column.

## Invoices

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/invoices?projectId=&clientId=&status=&overdue=&domain=&issuedFrom=&issuedTo=&page=1&pageSize=25` | `financial:view` | Newest first. `status` is `draft`, `sent`, `partial`, `paid` or `void`. `issuedFrom` and `issuedTo` (company days, both included) keep the invoices issued in that range, so drafts drop out |
| `GET /api/invoices/{id}` | `financial:view` | With lines, deliveries and who drafted it (`createdBy`) |
| `POST /api/projects/{id}/invoices` | `invoice:create` | A new draft: `{ lines: [{ description, amount }], termsDays, notes, billToName, billToContact }` |
| `PUT /api/invoices/{id}` | `invoice:create` | Replaces a draft's lines and details |
| `DELETE /api/invoices/{id}` | `invoice:create` | Discards a draft (deleted, with an `invoice_discarded` event) |
| `POST /api/invoices/{id}/issue` | `invoice:send` | `{ issuedOn }` (optional; today or earlier) numbers the draft and sets its due date |
| `GET /api/invoices/{id}/pdf` | `financial:view` | The invoice as a PDF (`hl-2026-014.pdf`); a draft is marked DRAFT, a voided invoice VOID. Downloading records nothing |
| `POST /api/invoices/{id}/email` | `invoice:send` | `{ to, note }` emails an issued invoice to the client with its PDF attached and records an `email` delivery. A draft or void invoice answers `422`. If sending fails, nothing is recorded (`503 email_delivery_failed`) |
| `POST /api/invoices/{id}/deliveries` | `invoice:send` | Records a delivery made outside Hourline: `{ channel: email \| whatsapp \| download, recipient, note }`. Repeatable |
| `POST /api/invoices/{id}/void` | `invoice:void` | `{ reason }`. Issued invoices only, and only while no live payment is attached |
| `POST /api/projects/{id}/invoice-plan` | `invoice:create` and `invoice:send` | Auto invoice (below): `{ invoices: [{ sendOn, amount, description }], termsDays, notes, sendTo }`. `201` with the invoices |
| `PUT /api/invoices/{id}/schedule` | `invoice:send` | `{ sendOn, sendTo }` gives a draft a day to go out on, or moves it |
| `DELETE /api/invoices/{id}/schedule` | `invoice:send` | Cancels a draft's schedule; it stays a draft |

Rules (`Business/Billing/InvoiceRules.cs`):

- Only **client projects with a client** are invoiced (`422 project_not_billable`). The invoice keeps that client even if the project's client later changes. Invoices are in the project's currency (USD).
- An invoice has 1–100 lines, each more than zero with at most 2 decimals. The total is their sum. Amounts are what the client pays, TVA included: TVA is never a line of its own (see "TVA" below).
- **Terms** default to the domain's billing settings (30 days before they exist).
- **Drafts** can be edited or discarded. Anything else is `422 invoice_not_draft`. A sent invoice is corrected by voiding it and issuing a new one.
- **Issuing** needs the domain's billing settings (`422 billing_settings_missing`). It takes the next number of the domain and the issue year, `PREFIX-YYYY-NNN`, from `invoice_sequences` under a row lock, so a number is never handed out twice. Drafts don't use up numbers. The issue date can be in the past but not the future (`400 issue_date_in_future`). Due date = issue date + terms. Calendar days are stored at noon UTC; an invoice issued today keeps its real time.
- **Status** is calculated: `void`, else `draft` until issued, else `paid` when live payments reach the total, `partial` when some are recorded, otherwise `sent`. Comparisons are exact, and in USD: payments are summed in USD (`amountUsd`), so an invoice in another currency is compared at its total in USD at the rate in force on its issue day (without one, its total as it stands). `isOverdue` means sent or part-paid with a due date before today. Balance = total − paid, never below zero, given in the invoice's currency (what is left in USD, at that same rate). A payment tied to an invoice is checked against its balance in USD the same way.
- Voiding keeps the number and the reason. It is refused while live payments are attached (`422 invoice_has_payments`).
- Events: `invoice_created`, `invoice_updated`, `invoice_discarded`, `invoice_issued`, `invoice_sent` (the channel in the payload), `invoice_voided` (the reason in the payload). The total is in the `amount` column.

**Who sees what was paid:** the invoice write endpoints (create, edit, issue, deliveries, void) answer with the invoice. For callers without `financial:view`, it leaves out `paid`, `balance` and `isOverdue`, and a partly or fully paid invoice reads as `sent`. They still see the amounts they invoice. The `payment_exceeds_balance` error doesn't state the balance.

## Auto invoice

What is left to bill, split into 2 to 24 invoices that go out on their own days (`Services/Billing/Implementation/InvoiceScheduleService.cs`):

- Each is a **draft with a one-line description, a day (`sendOn`, today or later, `400 send_date_in_past`) and an address (`sendTo`)**, scheduled by the caller (`scheduledBy`). The domain's billing settings must already exist (`422 billing_settings_missing`), since they are issued with nobody there. Until its day a scheduled draft is an ordinary draft: it can be edited, moved, sent by hand early or discarded. Issuing it in any way ends its schedule.
- **On its day**, from 9:00 company time, the hourly run issues it on that day and emails it with its PDF, signed with the name of whoever scheduled it, and records an `email` delivery ("Sent automatically, as scheduled"). Days already gone go out on the next run whatever the hour. A plan's invoices dated today go out as the plan is made.
- **Issued and emailed together:** if the email fails, nothing is saved, so it stays a scheduled draft and is tried again on every run. Whoever scheduled it is told once (`invoice_send_failed`).
- **Already paid:** what is on account pays it when it is issued, as for any invoice. One it settles is issued but not emailed: the client isn't sent a bill they have paid.
- **No address:** it isn't issued. On its day whoever scheduled it is told once to send it themselves (`invoice_send_due`), on WhatsApp for example.
- Whoever scheduled it is told when it goes out (`invoice_sent_automatically`, with `to` null when it wasn't emailed). It still goes out if they have left or lost the permission since: it was the company's decision.
- The "draft still waiting" reminder leaves scheduled drafts alone until their day has passed.
- Events: `invoice_created` (with `sendOn`), `invoice_scheduled` (`sendOn`, none when cancelled), and the usual `invoice_issued` and `invoice_sent`, with `automatic: true` when the run sent them.

## Reminders of overdue invoices

In a domain whose billing settings turn them on, the hourly run emails clients reminders of their overdue invoices (`Services/Billing/Implementation/InvoiceReminderService.cs`, rule in `InvoiceRules.NextReminderOn`):

- An invoice gets its first reminder `reminderFirstAfterDays` after its due date, then one every `reminderEveryDays` after the last, at most `reminderMax`. Today's go out from 9:00 company time; any due earlier go on the next run. They stop as soon as nothing is owed or the invoice is voided.
- Each goes to the address the invoice was last emailed to, otherwise the client's email contact (the primary one first), with the invoice PDF attached and what is still owed, signed "Accounts" and the company name.
- It is recorded as an `email` delivery with `isReminder` ("Automatic reminder 1 of 3"), which is what counts and spaces them, and as an `invoice_sent` event with `reminder` and `automatic: true` in the payload. The invoice row is locked while this is decided, so two API instances never send the same reminder.
- A failed email records nothing, so the next run tries again.
- **No email address:** whoever may send the domain's invoices (`invoice:send`) is told once (`invoice_reminder_due`) to chase it themselves, on WhatsApp for example. Automatic WhatsApp messages would need the WhatsApp Business Platform or a provider, and aren't built.

## Payments

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/payments?projectId=&clientId=&invoiceId=&includeVoided=&domain=&receivedFrom=&receivedTo=&page=1&pageSize=25` | `financial:view` | Most recently received first. Voided payments only with `includeVoided=true`. `receivedFrom` and `receivedTo` (both included) keep the payments received in that range |
| `GET /api/payments/{id}` | `financial:view` | A payment |
| `POST /api/projects/{id}/payments` | `payment:record` | `{ amount, currency, fxRate, receivedOn, method, reference, note, invoiceId, isInitial, accountLabelId }` |
| `PUT /api/payments/{id}/invoice` | `payment:record` | `{ invoiceId }` applies a payment on account to an issued invoice |
| `PUT /api/payments/{id}/label` | `payment:record` and `financial:view` (it answers with the whole payment) | `{ accountLabelId }` files a live payment under an account label, changes it, or clears it (`null` or left out) |
| `POST /api/payments/{id}/void` | `payment:void` | `{ reason }` |

Rules (`Business/Billing/PaymentRules.cs`):

- **No invoice is needed.** A payment with no `invoiceId` is held **on account**, whether or not anything has been invoiced or sent, in both domains. It counts as received for the project, and can later be applied to an issued invoice.
- **Issuing an invoice applies what is on account.** The project's live payments on account pay the new invoice, oldest received first, each one whole while it fits what the invoice has left to pay (payments are never split): a payment bigger than what is left stays on account. Each is recorded as `payment_applied` with `automatic: true`. This keeps an invoice the client has already paid from showing as owed or overdue.
- A payment tied to an invoice needs an issued, live invoice of the same project (`422 invoice_not_issued`, `422 invoice_voided`, `400` for another project's invoice). It can't be more than the invoice's balance (`422 payment_exceeds_balance`); record the rest on account. The invoice row is locked while the balance is checked.
- Only client projects with a client take payments (`422 project_not_billable`). The project's status doesn't matter: payments after completion are normal.
- **Currency:** USD or LBP. A payment keeps its amount, currency, the rate used and its USD value (amount ÷ rate, to cents). The rate is the one typed in (`fxRate`), otherwise the dated rate in force on the day received (`422 exchange_rate_missing` if there is none). USD uses 1. A payment worth less than a cent is refused (`400 payment_too_small`). Invoices, balances and project totals use the USD value.
- `receivedOn` is today or earlier (`400 received_date_in_future`). `method` is `bank` (default), `cheque`, `cash` or `other`.
- **Initial payment:** `isInitial` marks it. A project has at most one live initial payment (`422 initial_payment_exists`); voiding it frees the slot. A down payment is recorded like any other payment once the project exists: projects take no payment when they are created.
- **TVA share:** each payment keeps `tva_usd`, the TVA inside its `amount_usd` at the project's rate when it was recorded (`Tva.ShareOf`). Billing counts the whole amount; money reports count it net (see "TVA").
- Payments are never edited or deleted. They are voided with a reason (`422 payment_already_voided` twice). Only a live payment on account can be applied (`422 payment_already_applied`). The one thing about a payment that can be changed after it is recorded is its account label (below).
- **Account label.** A payment can be filed under one of the company's [account labels](company.md#account-labels) (`payments.account_label_id`, optional; Design payments take one too). It is given when the payment is recorded (`accountLabelId`) or later with `PUT /api/payments/{id}/label`, which sets, changes or clears it, and the payment shows it as `accountLabel: { id, name }` or `null`.
  - The label must exist (`400 validation_failed` naming `accountLabelId`; a zero or negative id too), and an archived label can't be newly picked (`422 account_label_archived`). A payment keeps an archived label it already has, and can drop it, but cannot get it back once cleared.
  - Setting the label a payment already has changes nothing and writes no event. A voided payment can't be relabelled (`422 payment_already_voided`). It follows the payment's domain: a payment of a domain the caller can't see reads as `404`.
  - The label decides the row a payment is counted in on an Execution site's [account figures](execution.md#account-figures). It changes no amount, TVA share, invoice or profit, so there is no profit floor.
- Events: `payment_recorded`, `payment_applied`, `payment_voided`, `payment_label_set` (the label's name and the previous one, `null` for none, in the payload). The USD value is in the `amount` column. The payload has only the invoice number, currency, method, initial flag, label names or reason, never the amount.

## A project's cash position

`money.billing` on a project (`Business/Billing/ProjectBilling.cs`), in USD. The same rule gives the billing figures of the project and client lists, the client pages and the Financials page ([finance.md](finance.md)); invoices are converted at the rate of their issue day everywhere.

| Field | Meaning |
|---|---|
| `invoiced` | Issued, live invoices |
| `outstanding` | Left to pay on those invoices |
| `received` | Every live payment, on account or not |
| `onAccount` | Live payments not tied to an invoice |
| `notYetInvoiced` | Revenue (Execution: the agreed amount) minus invoiced and minus what is held on account, never below zero: what still has to be billed or paid |
| `leftToInvoice` | Revenue minus invoiced, never below zero: what a new invoice can still be for (the project page only) |
| `owed` | Revenue minus received, never below zero |
| `paidAhead` | Received beyond the revenue |
| `issuedCount`, `paymentCount` | How many issued, live invoices and live payments |
| `overdue`, `overdueCount` | Left to pay on sent or partly paid invoices past their due date, and how many |
| `oldestOverdueDays` | Days past due of the oldest overdue invoice; null when none is overdue |
| `drafts` | The total of draft invoices, kept apart from invoiced |
| `unconvertedInvoices[]` | Invoices with no exchange rate on their issue day (a draft: today), per currency: left out of the USD figures above, never counted as 0 |

## TVA

Every client project carries a TVA rate (`projects.tva_rate`, `0.11` is 11%). A new client project, in any area, starts at
11% unless it is set otherwise; existing projects keep the rate they have. Projects from before TVA, and internal work, are at 0. The rules are in
`Business/Billing/Tva.cs`.

- **Setting it:** `tvaRate` (0 to 1) is set on `POST /api/projects`, `PUT …/payment` (Design) and `PUT …/pricing` (Execution).
  - Setting it needs `financial:view` (`403`).
  - A change of TVA alone is a change. It is kept in the payment history with its reason (`tvaRate`, `previousTvaRate`).
  - Making a project internal sets its rate to 0.
- **Net inside, gross for the client:**
  - Revenue, profit, margins, expected revenue and the money reports are **net**: TVA is collected for the state, never earned. Revenue in a period counts each payment less its TVA share, and the domain summary shows `tvaCollected` apart.
  - What the client sees and owes is **gross**, with TVA never shown as a line: the billing figures (owed, paid ahead, left to invoice, not yet invoiced), invoices, BOQs (the total to pay, with supervision and TVA) and quotes.
  - A project's money carries `tvaRate`, `paymentWithTva` (the payment, or a site's work price plus supervision, with TVA) and `revenueWithTva`.
- **Execution:** a site's **work price** is the agreed BOQs' total; **supervision** is the work price × the supervision rate (default 10%).
  - Revenue (the agreed amount) is work + supervision. The client pays that plus TVA on the work.
  - **TVA is on the work, never on supervision.** Example: 787,646.20 of work at 8% supervision and 11% TVA gives supervision 63,011.70, revenue 850,657.90, TVA 86,641.08 and 937,298.98 to pay.
  - A payment's TVA share is its share of the project's TVA by its share of what the client pays (`ExecutionPricing.TvaShareOf`); before any price is agreed it is the plain share of the rate.
  - Its cash profit counts payments net.

