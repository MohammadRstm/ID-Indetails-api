# Billing

Billing is a cash ledger kept apart from profitability: invoices and payments never change a project's revenue, cost or profit. Every amount has its currency beside it and nothing is converted silently. Invoice and payment status and balances are always calculated, never stored.

## Billing settings

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/billing-settings/{domain}` | `billing_settings:manage` or `financial:view` | The domain's settings, or `404 billing_settings_missing` before they are set up |
| `PUT /api/billing-settings/{domain}` | `billing_settings:manage` | Creates them the first time, replaces them afterwards |

- Each domain has its own settings: invoice prefix, default payment terms (0–365 days), bank account, bank name, IBAN, SWIFT, terms text and footer.
- The **invoice prefix** is 1–8 capital letters or digits. It can be changed at any time; invoices already issued keep their number. Invoice numbers are unique across the company, so two domains can't use the same prefix (`409 invoice_prefix_taken`).
- The settings are not seeded. No invoice can be issued in a domain until they exist.
- A domain the caller can't see, or an unknown domain, reads as `404`. Every save writes `billing_settings_updated`.

## Exchange rates

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/exchange-rates?currency=` | `financial:view` | Every dated rate, newest first per currency |

A rate says how many units of a currency one US dollar buys, from its effective date. The rate in force on a day is the latest one effective on or before it. Rates are company-wide.

## Invoices

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/invoices?projectId=&clientId=&status=&overdue=&domain=&page=1&pageSize=25` | `financial:view` | Newest first. `status` is `draft`, `sent`, `partial`, `paid` or `void` |
| `GET /api/invoices/{id}` | `financial:view` | With lines and deliveries |
| `POST /api/projects/{id}/invoices` | `invoice:create` | A new draft: `{ lines: [{ description, amount }], termsDays, notes, billToName, billToContact }` |
| `PUT /api/invoices/{id}` | `invoice:create` | Replaces a draft's lines and details |
| `DELETE /api/invoices/{id}` | `invoice:create` | Discards a draft (deleted, with an `invoice_discarded` event) |
| `POST /api/invoices/{id}/issue` | `invoice:send` | `{ issuedOn }` (optional; today or earlier) numbers the draft and sets its due date |
| `POST /api/invoices/{id}/deliveries` | `invoice:send` | Records a delivery: `{ channel: email \| whatsapp \| download, recipient, note }`. Repeatable |
| `POST /api/invoices/{id}/void` | `invoice:void` | `{ reason }`. Issued invoices only, and only while no live payment is attached |

Rules (`Business/Billing/InvoiceRules.cs`):

- Only **client projects with a client** are invoiced (`422 project_not_billable`). The invoice keeps that client even if the project's client later changes. Invoices are in the project's currency (USD).
- An invoice has 1–100 lines, each more than zero with at most 2 decimals. The total is their sum. There is no tax.
- **Terms** default to the domain's billing settings (30 days before they exist).
- **Drafts** can be edited or discarded. Anything else is `422 invoice_not_draft`. A sent invoice is corrected by voiding it and issuing a new one.
- **Issuing** needs the domain's billing settings (`422 billing_settings_missing`). It takes the next number of the domain and the issue year, `PREFIX-YYYY-NNN`, from `invoice_sequences` under a row lock, so a number is never handed out twice. Drafts don't use up numbers. The issue date can be in the past but not the future (`400 issue_date_in_future`). Due date = issue date + terms. Calendar days are stored at noon UTC; an invoice issued today keeps its real time.
- **Status** is calculated: `void`, else `draft` until issued, else `paid` when live payments reach the total, `partial` when some are recorded, otherwise `sent`. Comparisons are exact. `isOverdue` means sent or part-paid with a due date before today. Balance = total − paid, never below zero.
- Voiding keeps the number and the reason. It is refused while live payments are attached (`422 invoice_has_payments`).
- Events: `invoice_created`, `invoice_updated`, `invoice_discarded`, `invoice_issued`, `invoice_sent` (the channel in the payload), `invoice_voided` (the reason in the payload). The total is in the `amount` column.
