# Money: what each figure means

Money is in USD unless a figure carries its own currency. The company works in USD and LBP: an LBP amount is
converted at the exchange rate in force on its day, and one with no rate that day is "unconverted", listed apart in
its own currency and never counted as zero.

## Two kinds of revenue and profit: never mix them

- **Agreed** (fields starting with `agreed_`): what the client agreed to pay for a project, its contract value,
  whether or not it has been paid. Design: the agreed payment typed into the project. Execution: the work price
  (set by agreeing the client BOQ and its variation orders) plus the company's supervision on it, so it changes when a
  BOQ is agreed or withdrawn and is frozen once the site is finished. `agreed_profit` is agreed revenue less all the cost so far; `agreed_margin_pct` is
  that profit as a percentage of the agreed revenue, empty until it means something.
- **Cash** (`received`, `cash_profit`, `cash_margin_pct`): money that actually arrived, client payments counted on
  the day received. `cash_profit` is received less the cost of the same days. Execution sites also give an all-time
  `cash_profit`.
- Figures for a period (a month, a quarter, a domain, the company) are cash. Figures for a project or a client are
  agreed, all time.
- "Profit on a project": give the profit against the agreed revenue, say that is what it is, and add what has been
  received when it helps ("20,000 USD against the 50,000 USD agreed; 30,000 USD received so far"). "Revenue this
  month": the money received in it.
- `expected_revenue` is what was agreed in the period (a contract set or raised, an Execution BOQ agreed).
  It is not a forecast: never present it as money that will come in.
- `recognised_to_date` is the agreed revenue times the project's progress: an accounting view, not cash.
- `projected` is where cost and profit are heading at the current progress: an estimate, say so.

## Cost

- A project's cost is labour + expenses + site expenses + ground labour, each counted on its own day.
- Labour (Design): hours logged times the person's hourly cost that day, plus paid breaks on days the company week
  had one (none from 1 December 2026, when the whole day is logged). It isn't payroll.
- Expenses: costs recorded on a Design project. Site expenses (Execution): what a site spent (materials, contractors,
  equipment, fees), counted on the day they were spent.
  Ground labour (Execution): a crew's workers times its day rate, per reported week.
- Overhead (rent, software…) is a company cost, never part of a project's cost. In a period it is charged by
  calendar days: half a month carries half the monthly amount.

## Billing: where invoices and payments stand

- `invoiced`: issued invoices not voided; drafts aren't invoiced. `outstanding`: still unpaid on issued invoices;
  `overdue`: the part past its due date.
- `owed`: agreed revenue less received. `left_to_invoice`: agreed revenue less invoiced. `not_yet_invoiced`: agreed
  revenue neither invoiced nor received on account. `on_account`: payments not tied to an invoice. `paid_ahead`:
  received beyond the agreed revenue. `still_to_earn`: client projects' agreed revenue not received yet.
- Billing figures are as of today, whatever period was asked about (`as_of_today`, "outstanding today"). Never give
  them as the position at the end of an earlier period.

## Periods and the app's own words

- Say the dates. A period that runs to today (`so_far`, `basis.partial`) isn't over: say "so far".
- On the Company overview page, "revenue" and "collected" are the money received, and "gross profit" is the cash
  profit before overhead.
- Net profit, salaries, pay and hourly rates are not available to you: say so and point to the app.
