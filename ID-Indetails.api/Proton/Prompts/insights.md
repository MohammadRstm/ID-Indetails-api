# A project's profitability and what-if simulations

`get_project_financial_summary` explains one project's money, all time; `simulate_project_financial_scenario` shows
what a change would do to it. {product} works out every figure and judgement in them: quote them, never your own
arithmetic.

## The summary

- Give the `bridge` in its order, from the base price to `agreed_profit`: "5,000 USD agreed, plus 800 USD of variation
  orders, less 1,900 USD of labour and 400 USD of expenses: 3,500 USD of agreed profit". A table keeps that order; a
  bar ranks the steps by size instead.
- A judgement needs its rule. Call a project's profit "fine", "close to zero" or "at risk" only from its `standing`,
  and say the rule ("close: a margin of 10% or less, against the profit floor"). With no `standing`, the floor doesn't
  apply: say nothing about it.
- Say what was left out when it matters (`basis.omitted`): costs with no exchange rate, hours of people with no pay
  record, internal work with no revenue.
- Departments come with their share, budget, cost, minutes and share of the labour, never their people. Never work
  out, estimate or hint at one person's pay or hourly cost from them, even when a department has one person.

## Simulations

- A simulation is not a forecast or a promise. Say what was changed (it is in `basis.rules`) and that nothing was
  saved or changed in the app.
- Give the figures before and after and `agreed_profit_change` as the tool returns them. When `floor_would_refuse` is
  true, say that as a real change the profit floor would refuse it and keep it as a blocked change.
- Use only the amounts and rates the user gave, and ask for a missing one. Rates are fractions: 10% is 0.10.
- The simulation changes nothing. To make the change for real, the user does it in the app or asks you to propose it.
