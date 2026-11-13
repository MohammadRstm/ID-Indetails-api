# Money: what each figure means

Money is in USD unless a figure carries its own currency. The company works in USD and LBP: an LBP amount is
converted at the exchange rate in force on its day, and one with no rate that day is "unconverted", listed apart in
its own currency and never counted as zero.

## Three profits: never mix them

- **Agreed** (fields starting with `agreed_`): what the client agreed to pay for a project, its contract value,
  whether or not it has been paid. Design: the customer payment typed into the project plus its applied variation orders (VOs, work added or taken out). Execution: the work price
  (set by agreeing the client BOQ and its variation orders) plus the company's supervision on it, plus the BOQs' permit
  lines (charged as they are), so it changes when a
  BOQ is agreed or withdrawn and is frozen once the site is finished. `agreed_profit` is agreed revenue less all the cost so far; `agreed_margin_pct` is
  that profit as a percentage of the agreed revenue, empty until it means something.
- **Period cash** (`received`, `period_cash_profit`, `cash_margin_pct`): money that actually arrived, client payments
  counted on the day received, less the TVA in them. `period_cash_profit` is received less the cost of the same days;
  pocket money plays no part in it.
- **Site cash** (`site_cash_profit`, Execution sites only): all time, received less cost, less what was moved to the
  pocket money, plus what the pocket money paid in. It is not a period figure: never add it up with
  `period_cash_profit` or compare the two as if they were the same.
- Figures for a period (a month, a quarter, a domain, the company) are cash. Figures for a project or a client are
  agreed, all time.
- "Profit on a project": give the profit against the agreed revenue, say that is what it is, and add what the client
  has paid when it helps, saying that includes TVA ("20,000 USD profit against the 50,000 USD agreed; the client has
  paid 33,300 USD so far, TVA included"). "Revenue this month": the money received in it, without its TVA.
- `expected_revenue` is what was agreed in the period (a contract set or raised, an Execution BOQ agreed).
  It is not a forecast: never present it as money that will come in.
- `recognised_to_date` is the agreed revenue times the project's progress: an accounting view, not cash.
- `projected` is where cost and profit are heading at the current progress: an estimate, say so.
- `bridge`: where a project's `agreed_profit` comes from, step by step, net of TVA: the base price, what changed it
  (variation orders, a settlement, an overpayment kept), a site's supervision and permits, each cost (negative) and the
  pocket money. Its steps add up to `agreed_profit`.
- `standing`: a client project's profit against the profit floor: `at_zero` (0 or below), `close` (a margin of 10% or
  less) or `fine`. Absent where the floor doesn't apply (internal work, no agreed amount yet).
- Risk register: a business's client projects with a financial risk, most pressing first, each risk with the fact
  measured and the rule that fired. A project not on it had no rule fire; internal projects are never on it.
- `baseline`: a category's average month of spending over the months before (a month with nothing counts as 0), or an
  item's average unit cost over its last purchases: what spending is judged against. Absent while there isn't enough
  history.
- Runway: how long a site's cash profit lasts at today's crew and day rate, in working days (Monday to Saturday). A
  site whose cash profit is already below 0 has spent more than its client paid.
- Outside scope: hours an admin marked as work outside a project's agreed scope. Its labour cost and its share of the
  labour cost (`outside_scope_pct`) come from `analyze_labor_costs`, never one person's.
- Utilization: the hours someone logged ÷ the hours there were to log (expected less excused), in a month. Hours only,
  never money and never a judgement of the person.

## TVA

- TVA (11% by default on client projects) is collected for the state, never earned. Revenue, profit, margins,
  expected revenue and every period figure are **net** of it: a period's `received` is its payments less their TVA,
  and `tva_collected` is that TVA.
- What the client is billed, pays and owes is **gross**, TVA included: `agreed_with_tva`, the billing figures
  (`billing`, `as_of_today`), invoices and payments. Never compare a gross figure with a net one without saying so.
- Execution: TVA is on the work price only, never on the supervision or the permits.

## Execution sites

- **Supervision**: the company's fee on a site, its work price × the supervision rate (10% by default), part of the
  agreed revenue. Permits carry no supervision. Supervision the company takes out of a site's account isn't a cost.
- **Pocket money**: money super admins move out of sites by hand and pay back to sites that run short. It moves a
  site's profit (`to_pocket` lowers it, `from_pocket` raises it), never what the client owes.

## Cost

- A project's cost is labour + expenses + site expenses + ground labour, each counted on its own day.
- Labour (Design): hours logged times the person's hourly cost that day, plus paid breaks on days the company week
  had one (none from 1 December 2026, when the whole day is logged). It isn't payroll.
- Expenses: costs recorded on a Design project. Site expenses (Execution): what a site spent (materials, contractors,
  equipment, fees), counted on the day they were spent.
  Ground labour (Execution): a crew's workers times its day rate, per reported week.
- Overhead (rent, software…) is a company cost, never part of a project's cost. In a period it is charged by
  calendar days: half a month carries half the monthly amount. A one-off company expense (a refrigerator, a repair)
  is paid once: it counts in full on the day it was spent, in the period that day falls in.

## Billing: where invoices and payments stand

- `invoiced`: issued invoices not voided; drafts aren't invoiced. `outstanding`: still unpaid on issued invoices;
  `overdue`: the part past its due date.
- Billing is measured against what the client is agreed to pay with TVA (`agreed_with_tva`). `owed`: that less
  received. `left_to_invoice`: that less invoiced. `not_yet_invoiced`: what is neither invoiced nor received on
  account. `on_account`: payments not tied to an invoice. `paid_ahead`: received beyond it. `still_to_earn`: client
  projects' agreed revenue not received yet.
- Billing figures are as of today, whatever period was asked about (`as_of_today`, "outstanding today"). Never give
  them as the position at the end of an earlier period.
- `settled_owed`: what the client still owed when the project was completed, settled then: `excused` (a discount
  agreed with them) or `written_off` (revenue lost). Either dropped the price to what was received, so the agreed
  revenue and profit fell with it and nothing is owed any more.

## Periods and the app's own words

- Say the dates. A period that runs to today (`so_far`, `basis.partial`) isn't over: say "so far".
- On the Company overview page, "revenue" and "collected" are the money received, and "gross profit" is the cash
  profit before the company's expenses.
- Net profit, salaries, pay and hourly rates are not available to you: say so and point to the app.
