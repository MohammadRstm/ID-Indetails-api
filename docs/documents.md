# Documents: PDFs and emailing them

Invoices, client statements and project reports are made as PDFs on the server (decision O3), with PDFsharp and MigraDoc (MIT licence). The layout is a plain first version. It will be refined later, without changing the endpoints.

## Endpoints

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/invoices/{id}/pdf` | `financial:view` | The invoice (`hl-2026-014.pdf`). A draft is marked DRAFT and a voided invoice VOID |
| `POST /api/invoices/{id}/email` | `invoice:send` | `{ to, note }`: emails an issued invoice with its PDF attached, then records an `email` delivery (`201`, the invoice) |
| `GET /api/statements/{id}/pdf` | `statement:create`, `statement:send` or `financial:view` | The statement (`st-2026-003.pdf`) |
| `POST /api/statements/{id}/email` | `statement:send` | `{ to, note }`: emails the statement with its PDF attached, then records an `email` delivery (`200`, the statement) |
| `GET /api/projects/{id}/report?kind=&from=&to=&sections=` | `financial:view` and `work_entry:view_all` | A Design project's report (below) |

- **Downloading a PDF records nothing.** To mark an invoice or statement as sent by hand, record a `download` or `whatsapp` delivery (`POST …/deliveries`), as before.
- **Emailing:**
  - `to` is any valid address. It is usually one of the client's contacts, but it doesn't have to be.
  - `note` is kept with the delivery and isn't put in the email.
  - The email is sent first. If it fails, nothing is recorded and the answer is `503 email_delivery_failed`.
  - A draft or voided invoice can't be emailed (`422 invoice_not_issued` / `invoice_voided`).
- **Wording:** documents are in English only. They have no Arabic, since the company's invoices don't use it.

## What each document shows

Every document opens with the company's letterhead (name, website and address from the company settings, and the logo when it is a PNG or JPEG). Every page ends with a footer line and "Page X of Y". Long tables run onto more pages and repeat their header row. Money is written in full (`$1,250.00`, `1,500,000 LBP`), and dates as `Sep 30, 2026` in the company time zone.

- **Invoice:**
  - Billed to (`billToName` or the client, and `billToContact`), the project, the issue date and the due date.
  - The lines, the subtotal and total, what was received to date (for callers with `financial:view`), and the amount due.
  - The note, and how to pay: the domain's bank details, the payment terms, the billing settings' terms text and footer.
  - Only client-facing fields are read: never rates, labour, hours, expenses, profit or margin.
- **Statement:**
  - The same rows as `GET /api/statements/{id}`: one line for a total-only statement; one row per material type for a detailed one, split into earlier and new amounts when earlier materials are included, with unlisted types on one line.
  - Then the totals and the payment summary. It ends by saying it is not an invoice.
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
- Each document has its own class: `InvoicePdf`, `StatementPdf`, `ProjectReportPdf`.
- `DocumentEmails` holds the email wording.
- `DocumentService` loads the data through the invoice, statement and project services, so their visibility and what each caller may see still apply. It then renders the PDF and sends it.
