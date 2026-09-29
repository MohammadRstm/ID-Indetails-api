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
| `POST /api/exchange-rates` | `exchange_rate:manage` (super admins) | `{ currency, ratePerUsd, effectiveFrom }` adds a dated rate |

A rate says how many units of a currency one US dollar buys, from its effective date. The rate in force on a day is the latest one effective on or before it. Rates are company-wide, so only super admins set them.

Rates are append-only, like pay history (`Business/Billing/ExchangeRateRules.cs`): a new rate applies from today or earlier (`400 rate_date_in_future`) and after the currency's latest rate (`422 rate_not_after_latest`). Payments already recorded keep the rate they were recorded with. Adding one writes `exchange_rate_set` with no domain and the rate in the `amount` column.

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

**Who sees what was paid:** the invoice write endpoints (create, edit, issue, deliveries, void) answer with the invoice. For callers without `financial:view`, it leaves out `paid`, `balance` and `isOverdue`, and a partly or fully paid invoice reads as `sent`. They still see the amounts they invoice. The `payment_exceeds_balance` error doesn't state the balance.

## Payments

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/payments?projectId=&clientId=&invoiceId=&includeVoided=&domain=&page=1&pageSize=25` | `financial:view` | Most recently received first. Voided payments only with `includeVoided=true` |
| `GET /api/payments/{id}` | `financial:view` | A payment |
| `POST /api/projects/{id}/payments` | `payment:record` | `{ amount, currency, fxRate, receivedOn, method, reference, note, invoiceId, isInitial }` |
| `PUT /api/payments/{id}/invoice` | `payment:record` | `{ invoiceId }` applies a payment on account to an issued invoice |
| `POST /api/payments/{id}/void` | `payment:void` | `{ reason }` |

Rules (`Business/Billing/PaymentRules.cs`):

- **No invoice is needed.** A payment with no `invoiceId` is held **on account**, whether or not anything has been invoiced or sent, in both domains. It counts as received for the project, and can later be applied to an issued invoice.
- A payment tied to an invoice needs an issued, live invoice of the same project (`422 invoice_not_issued`, `422 invoice_voided`, `400` for another project's invoice). It can't be more than the invoice's balance (`422 payment_exceeds_balance`); record the rest on account. The invoice row is locked while the balance is checked.
- Only client projects with a client take payments (`422 project_not_billable`). The project's status doesn't matter: payments after completion are normal.
- **Currency:** USD or LBP. A payment keeps its amount, currency, the rate used and its USD value (amount ÷ rate, to cents). The rate is the one typed in (`fxRate`), otherwise the dated rate in force on the day received (`422 exchange_rate_missing` if there is none). USD uses 1. A payment worth less than a cent is refused (`400 payment_too_small`). Invoices, balances and project totals use the USD value.
- `receivedOn` is today or earlier (`400 received_date_in_future`). `method` is `bank` (default), `cheque`, `cash` or `other`.
- **Initial payment:** `isInitial` marks it. A project has at most one live initial payment (`422 initial_payment_exists`); voiding it frees the slot. Execution projects can record it when they are created.
- Payments are never edited or deleted. They are voided with a reason (`422 payment_already_voided` twice). Only a live payment on account can be applied (`422 payment_already_applied`).
- Events: `payment_recorded`, `payment_applied`, `payment_voided`. The USD value is in the `amount` column. The payload has only the invoice number, currency, method, initial flag or reason, never the amount.

## A project's cash position

`money.billing` on a project (`Business/Billing/ProjectBilling.cs`), in USD:

| Field | Meaning |
|---|---|
| `invoiced` | Issued, live invoices |
| `outstanding` | Left to pay on those invoices |
| `received` | Every live payment, on account or not |
| `onAccount` | Live payments not tied to an invoice |
| `notYetInvoiced` | Revenue (Execution: the agreed amount) minus invoiced, never below zero |
| `owed` | Revenue minus received, never below zero |
| `paidAhead` | Received beyond the revenue |
