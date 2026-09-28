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
