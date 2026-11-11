# Documents: PDFs and emailing them

Invoices, client BOQs and variation orders, project reports, and the salaries and schedules lists are made as PDFs on the server (decision O3), with PDFsharp and MigraDoc (MIT licence). The layout is a plain first version. It will be refined later, without changing the endpoints.

## Endpoints

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/invoices/{id}/pdf` | `financial:view` | The invoice (`hl-2026-014.pdf`). A draft is marked DRAFT and a voided invoice VOID |
| `POST /api/invoices/{id}/email` | `invoice:send` | `{ to, note }`: emails an issued invoice with its PDF attached, then records an `email` delivery (`201`, the invoice) |
| `GET /api/boqs/{id}/pdf` | `financial:view` or `project:pricing_manage` | A client BOQ or VO (`bq-2026-001.pdf`, `vo-2026-001.pdf`). An internal draft has none (`422 boq_internal`) |
| `POST /api/boqs/{id}/email` | `project:pricing_manage` | `{ to, note }`: emails the BOQ with its PDF attached, then records an `email` delivery (`200`, the BOQ). Refused before anything is sent when the BOQ is internal, empty or on a finalised site |
| `GET /api/design-vos/{id}/pdf` | `financial:view` | A Design variation order (`vo-2026-003.pdf`): its changes, the total before TVA, the TVA at the project's rate and the total, the hours outside the agreed scope when it shows them, and "FOR APPROVAL" while it is pending |
| `POST /api/design-vos/{id}/email` | `financial:write` | `{ to, note }`: emails a pending or applied Design VO with its PDF attached (`204`); a cancelled one isn't sent |
| `GET /api/projects/{id}/report?kind=&from=&to=&sections=` | `financial:view` and `work_entry:view_all` | A Design project's report (below) |
| `GET /api/company/salaries/pdf?month=&domain=&role=&search=` | `salary:view` | A month of salaries as a landscape PDF (`salaries-2026-11.pdf`, `salaries-2026-11-design.pdf` with a business): the same filters and people as the CSV export (10 a minute per user) |
| `GET /api/company/schedules/pdf?domain=&search=` | `schedule:manage` | Everyone's week as a landscape PDF (`schedules-2026-11-11.pdf`, dated today in company time) (10 a minute per user) |

- **Downloading a PDF records nothing.** To mark an invoice or BOQ as sent by hand, record a `download` or `whatsapp` delivery (`POST …/deliveries`), as before.
- **Emailing:**
  - `to` is any valid address. It is usually one of the client's contacts, but it doesn't have to be.
  - `note` is kept with the delivery and isn't put in the email.
  - The email is sent first. If it fails, nothing is recorded and the answer is `503 email_delivery_failed`.
  - A draft or voided invoice can't be emailed (`422 invoice_not_issued` / `invoice_voided`).
- **Wording:** documents are in English only. They have no Arabic, since the company's invoices don't use it.

Design has no quotes: they were removed in November 2026, with their records. A Design project's price is set when it
is created and corrected by holders of `financial:write`.

## What each document shows

Every document opens with the company's letterhead (name, website and address from the company settings, and the logo when it is a PNG or JPEG). Every page of what the client receives (invoices, client BOQs and VOs, and the client project summary) has the company's mark behind it (`PdfWatermark`): the split "0" of the logo in pale green, half of it at the middle of the right edge and a quarter of it in the bottom-left corner, both fading out from the page edge before the content. It is drawn once as a faded picture behind the text, which stays text. The internal project report has none. Every page ends with a footer line and "Page X of Y". Long tables run onto more pages and repeat their header row. Money is written in full (`$1,250.00`, `1,500,000 LBP`), and dates as `Sep 30, 2026` in the company time zone.

- **Invoice:**
  - Billed to (`billToName` or the client, and `billToContact`), the project, the issue date and the due date.
  - The lines, the subtotal and total, what was received to date (for callers with `financial:view`), and the amount due.
  - The note, and how to pay: the domain's bank details, the payment terms, the billing settings' terms text and footer.
  - Only client-facing fields are read: never rates, labour, hours, expenses, profit or margin.
- **BOQ or VO:**
  - The company header, the client and project, the number, the date (the day it was agreed, or today for a draft) and the title; a VO also its reason. A draft is stamped DRAFT.
  - A client BOQ opens, above its items, with a fixed welcome naming the client and the project: "Dear {client}, thank you for trusting us with your {project}. This bill of quantities sets out the work we discussed, item by item, for your review." A VO has none.
  - One row per line: its code, description, **final quantity** (the quantity plus waste), unit, rate and total. The exact quantity and the waste are never shown, in the PDF or the email.
  - Then the subtotal and the adjustment (only when there is one), the total, supervision (x%), TVA (y%) and the total to pay, from the same figures as `GET /api/boqs/{id}`. See [execution.md](execution.md#boqs-and-variation-orders).
- **Project report** (Design projects only; an Execution site answers `422 not_for_this_domain`):
  - `kind=internal` (the default) has everything. `kind=client` is the client summary: the work, expenses and financial sections are dropped, and the summary loses cost, profit and margin.
  - `from` and `to` narrow the work counted (hours, team, the work table and the log). They default to the project's start and today, or its completion day. Money is always the whole project.
  - `sections` (repeat the key) picks from: `summary`, `info`, `timeline`, `work`, `expenses`, `financials`, `billing`, `client`, `log`. The default is all of them except `log`, which lists the 12 most recent entries. `billing` is left out of internal projects.
  - The figures come from the same services as the project page (project money, project work, timeline, expenses, invoices, payments, client), so the report and the page always agree.
  - The prototype's "Performance" charts are not in the PDF yet.
- **Salaries** (internal, landscape, no watermark; its footer says it contains pay information):
  - The title "Salaries · November 2026", "In progress, to Nov 11, 2026" for the current month (otherwise "Closed month") and the filters in force (business, role, search).
  - One row per person: name and department, pay basis (with the salary or hourly rate), expected hours, hours logged (in schedule, made up and overtime; "Not tracked" for people who don't log hours), overtime paid, short hours, base pay, deduction, overtime pay and what is to pay, each in the person's currency.
  - A total row per currency, then "No pay set: …" naming the filtered people with no pay.
  - It is `GET /api/company/salaries` with every row on one page (`SalaryService.ListAsync`), so the list, the CSV and the PDF can't disagree.
- **Schedules** (internal, landscape, no watermark; `GET /api/company/schedules/pdf`, and the same people as CSV at `GET /api/company/schedules/export`):
  - The title "Schedules", "As of Nov 11, 2026", the filters, and the company default week (working days, hours, break, hours a week).
  - People grouped as the Schedules page groups them: each business's departments alphabetically (Design, then Execution), then people with no department (named with their business when the caller sees both), then the company's own people (super admins).
  - Each person: position, "Own schedule" or "Company default", "Changes on …" when a change is coming, each weekday's hours (in the company's 12 or 24-hour clock, or "Off") and hours a week. Week columns start on the company's week start.
  - The CSV has one row per person in the same order: Name, Business, Department, Position, Schedule (Own or Default), a column per weekday from the week start (`09:00-17:00` or `Off`, always 24-hour), Hours a week (decimal) and Next change (`yyyy-MM-dd`). Names starting with `=`, `+`, `-` or `@` are saved safe from spreadsheets running them as formulas.
  - The figures come from `ScheduleService.ListAsync`, the same as the page.

## Fonts

Every font resolves to Segoe WP, which ships inside the PDFsharp package (`Services/Documents/Implementation/DocumentFonts.cs`). A PDF therefore looks the same on any server, with no fonts to install. To use the company's own font later, give that class the font files instead.

## Code

- `Services/Documents/Implementation/PdfLayout.cs` holds the page, letterhead, tables, totals, footer, and money and date formats.
- Each document has its own class: `InvoicePdf`, `BoqPdf`, `DesignVoPdf`, `ProjectReportPdf`, `SalariesPdf`, `SchedulesPdf` (and `SchedulesCsv`). `PdfLayout.NewDocument(landscape: true)` makes an A4 page on its side (`LandscapeContentWidthCm`) for the wide tables.
- `DocumentEmails` builds the emails. An invoice or BOQ email shows the document itself (the same client-facing fields as the PDF: lines, totals, notes, how to pay) and attaches the PDF to keep or print.
- Every email, account emails included, is drawn in one frame (`Services/Email/Implementation/EmailLayout.cs`): the logo, a white card and a footer, built from tables with inline styles because mail apps ignore style sheets. The logo (`Assets/email-logo.png`) is built into the assembly and travels in the email as an inline image, so it shows even where pictures from the web are blocked. Each email also has a plain-text version.
- `DocumentService` loads the data through the invoice, BOQ and project services, so their visibility and what each caller may see still apply. It then renders the PDF and sends it.
