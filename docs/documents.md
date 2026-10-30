# Documents: PDFs and emailing them

Invoices, quotes, client BOQs and variation orders, and project reports are made as PDFs on the server (decision O3), with PDFsharp and MigraDoc (MIT licence). The layout is a plain first version. It will be refined later, without changing the endpoints.

## Endpoints

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/invoices/{id}/pdf` | `financial:view` | The invoice (`hl-2026-014.pdf`). A draft is marked DRAFT and a voided invoice VOID |
| `POST /api/invoices/{id}/email` | `invoice:send` | `{ to, note }`: emails an issued invoice with its PDF attached, then records an `email` delivery (`201`, the invoice) |
| `GET /api/boqs/{id}/pdf` | `financial:view` or `project:pricing_manage` | A client BOQ or VO (`bq-2026-001.pdf`, `vo-2026-001.pdf`). An internal draft has none (`422 boq_internal`) |
| `POST /api/boqs/{id}/email` | `project:pricing_manage` | `{ to, note }`: emails the BOQ with its PDF attached, then records an `email` delivery (`200`, the BOQ). Refused before anything is sent when the BOQ is internal, empty or on a finalised site |
| `GET /api/projects/{id}/report?kind=&from=&to=&sections=` | `financial:view` and `work_entry:view_all` | A Design project's report (below) |

- **Downloading a PDF records nothing.** To mark an invoice, quote or BOQ as sent by hand, record a `download` or `whatsapp` delivery (`POST …/deliveries`), as before.
- **Emailing:**
  - `to` is any valid address. It is usually one of the client's contacts, but it doesn't have to be.
  - `note` is kept with the delivery and isn't put in the email.
  - The email is sent first. If it fails, nothing is recorded and the answer is `503 email_delivery_failed`.
  - A draft or voided invoice can't be emailed (`422 invoice_not_issued` / `invoice_voided`).
- **Wording:** documents are in English only. They have no Arabic, since the company's invoices don't use it.

## Quotes

A quote tells a Design client what the whole project costs them: the agreed payment, with TVA in it and never shown apart,
and the project's description as its scope. Execution has no quotes: its price is built from BOQs. Each is a
snapshot (`quotes`, numbered `QU-YYYY-NNN` per year in `quote_sequences`): a change of payment or TVA makes a new one.
Sends are kept in `quote_deliveries`.

| Method and path | Needs | Purpose |
|---|---|---|
| `POST /api/projects/{id}/quotes` | `invoice:send` | Makes a quote from the payment and TVA now (`422 quote_not_possible` for internal work or no agreed amount, `422 quote_not_for_execution` for an Execution site) |
| `GET /api/projects/{id}/quotes` | `invoice:send` | The project's quotes, newest first |
| `GET /api/quotes/{id}` / `…/pdf` | `invoice:send` | One quote; its PDF |
| `POST /api/quotes/{id}/email` | `invoice:send` | Emails it to the client with the PDF, and records the delivery |
| `POST /api/quotes/{id}/deliveries` | `invoice:send` | Records a WhatsApp send or a download |

When someone without `invoice:send` creates a Design client project, holders of it get `quote_due` (never for an Execution site): whether to send it a
quote is theirs to decide. Someone with it is asked in the app right after creating the project or changing its
payment or TVA.

## What each document shows

Every document opens with the company's letterhead (name, website and address from the company settings, and the logo when it is a PNG or JPEG). Every page ends with a footer line and "Page X of Y". Long tables run onto more pages and repeat their header row. Money is written in full (`$1,250.00`, `1,500,000 LBP`), and dates as `Sep 30, 2026` in the company time zone.

- **Invoice:**
  - Billed to (`billToName` or the client, and `billToContact`), the project, the issue date and the due date.
  - The lines, the subtotal and total, what was received to date (for callers with `financial:view`), and the amount due.
  - The note, and how to pay: the domain's bank details, the payment terms, the billing settings' terms text and footer.
  - Only client-facing fields are read: never rates, labour, hours, expenses, profit or margin.
- **BOQ or VO:**
  - The company header, the client and project, the number, the date (the day it was agreed, or today for a draft) and the title; a VO also its reason. A draft is stamped DRAFT.
  - One row per line: its code, description, **final quantity** (the quantity plus waste), unit, rate and total. The exact quantity and the waste are never shown, in the PDF or the email.
  - Then the subtotal and the adjustment (only when there is one), the total, supervision (x%), TVA (y%) and the total to pay, from the same figures as `GET /api/boqs/{id}`. See [execution.md](execution.md#boqs-and-variation-orders).
- **Project report** (Design projects only; an Execution site answers `422 not_for_this_domain`):
  - `kind=internal` (the default) has everything. `kind=client` is the client summary: the work, expenses and financial sections are dropped, and the summary loses cost, profit and margin.
  - `from` and `to` narrow the work counted (hours, team, the work table and the log). They default to the project's start and today, or its completion day. Money is always the whole project.
  - `sections` (repeat the key) picks from: `summary`, `info`, `timeline`, `work`, `expenses`, `financials`, `billing`, `client`, `log`. The default is all of them except `log`, which lists the 12 most recent entries. `billing` is left out of internal projects.
  - The figures come from the same services as the project page (project money, project work, timeline, expenses, invoices, payments, client), so the report and the page always agree.
  - The prototype's "Performance" charts are not in the PDF yet.

## Fonts

Every font resolves to Segoe WP, which ships inside the PDFsharp package (`Services/Documents/Implementation/DocumentFonts.cs`). A PDF therefore looks the same on any server, with no fonts to install. To use the company's own font later, give that class the font files instead.

## Code

- `Services/Documents/Implementation/PdfLayout.cs` holds the page, letterhead, tables, totals, footer, and money and date formats.
- Each document has its own class: `InvoicePdf`, `QuotePdf`, `BoqPdf`, `ProjectReportPdf`.
- `DocumentEmails` builds the emails. An invoice, quote or BOQ email shows the document itself (the same client-facing fields as the PDF: lines, totals, notes, how to pay) and attaches the PDF to keep or print.
- Every email, account emails included, is drawn in one frame (`Services/Email/Implementation/EmailLayout.cs`): the logo, a white card and a footer, built from tables with inline styles because mail apps ignore style sheets. The logo (`Assets/email-logo.png`) is built into the assembly and travels in the email as an inline image, so it shows even where pictures from the web are blocked. Each email also has a plain-text version.
- `DocumentService` loads the data through the invoice, quote, BOQ and project services, so their visibility and what each caller may see still apply. It then renders the PDF and sends it.
