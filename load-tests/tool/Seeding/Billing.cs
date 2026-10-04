using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Enums;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Invoices and payments. Design client projects are invoiced on the first working day of each month, a share of
/// the agreed revenue at a time; a few use an invoice plan that the scheduled checks issue and email on their days.
/// Execution sites get a progress invoice every four weeks. Most clients pay in full within weeks, some in two parts,
/// some not at all; now and then an invoice is voided and issued again.
/// </summary>
public sealed class Billing(World world, ApiClient api, SimulatedClock clock)
{
    private int _references;

    private Random Random => world.Random;

    public async Task DaytimeAsync(DateOnly day)
    {
        if (!World.IsWorkingDay(day))
        {
            return;
        }

        clock.MoveTo(day, 11, 30);
        if (World.IsFirstWorkingDayOfMonth(day) && day > world.Plan.FirstDay)
        {
            foreach (var project in world.Projects.Where(p => p.Domain == "design" && p.IsClientWork && !p.AutoInvoiced && p.Status != "archived"))
            {
                await InvoiceDesignAsync(project, day);
            }
        }

        foreach (var site in world.Projects.Where(p => p.Domain == "execution" && p.Status == "active" && day.DayNumber - p.LastInvoiceOn.DayNumber >= 28))
        {
            await InvoiceSiteAsync(site, day);
        }

        foreach (var project in world.Projects.Where(p => p.Domain == "design" && p.IsClientWork && p.StartedOn == day && Random.Chance(0.05)).ToList())
        {
            await PlanInvoicesAsync(project, day);
        }

        clock.MoveTo(day, 12, 30);
        await ReceivePaymentsAsync(day);
    }

    private async Task InvoiceDesignAsync(SeedProject project, DateOnly day)
    {
        var left = project.Revenue - project.Invoiced;
        if (left <= 0 || (project.Status == "completed" && project.CompleteOn < day.AddDays(-31)))
        {
            return;
        }

        var amount = project.Status == "completed" ? left : Math.Min(left, Random.Amount(project.Revenue * 0.2m, project.Revenue * 0.35m, 50m));
        var design = decimal.Round(amount * 0.7m, 0);
        await IssueAsync(project, day,
        [
            new InvoiceLineRequest { Description = $"Design services, {day.AddMonths(-1):MMMM yyyy}", Amount = design },
            new InvoiceLineRequest { Description = "Drawings and coordination", Amount = amount - design },
        ]);
    }

    /// <summary>A quarter of the fee and of the materials' client value so far.</summary>
    private async Task InvoiceSiteAsync(SeedProject site, DateOnly day)
    {
        site.LastInvoiceOn = day;
        var amount = decimal.Round((site.Fee + site.MaterialCost * (1 + site.Markup)) * 0.25m / 50m, 0) * 50m;
        if (amount > 0)
        {
            await IssueAsync(site, day, [new InvoiceLineRequest { Description = $"Works progress to {day:d MMM yyyy}", Amount = amount }]);
        }
    }

    private async Task IssueAsync(SeedProject project, DateOnly day, IReadOnlyList<InvoiceLineRequest> lines)
    {
        var admin = Invoicer(project, day);
        var total = lines.Sum(l => l.Amount);
        var created = await api.PostAsync(admin.Token, $"/api/projects/{project.Id}/invoices", new SaveInvoiceRequest
        {
            Lines = lines, Notes = "LT load-test invoice.", BillToName = project.Client!.Name, BillToContact = project.Client.Email,
        });
        world.Tally.Add("invoices");
        project.Invoiced += total;

        // A few stay drafts for a while, as some do.
        if (Random.Chance(0.06))
        {
            return;
        }

        var invoice = new SeedInvoice(created.Id(), project, total);
        var issued = await api.PostAsync(admin.Token, $"/api/invoices/{invoice.Id}/issue");
        invoice.IssuedOn = day;
        // Issuing applies what the client paid on account (an Execution site's initial payment), as the API does.
        invoice.Paid = total - issued.GetProperty("balance").GetDecimal();
        world.Tally.Add("invoices.issued");
        await api.PostAsync(admin.Token, $"/api/invoices/{invoice.Id}/deliveries", Random.Chance(0.8)
            ? new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = project.Client.Email }
            : new RecordDeliveryRequest { Channel = DeliveryChannel.Download });

        // Only an invoice nobody has paid anything against can be voided.
        if (invoice.Paid == 0 && Random.Chance(0.02))
        {
            await api.PostAsync(admin.Token, $"/api/invoices/{invoice.Id}/void", new VoidRequest { Reason = "LT issued with the wrong amount" });
            world.Tally.Add("invoices.voided");
            project.Invoiced -= total;
            await IssueAsync(project, day, lines);
            return;
        }

        PlanPayments(invoice, day);
        world.Invoices.Add(invoice);
    }

    /// <summary>Most pay what is left in full within weeks, some in two parts, some never during the history.</summary>
    private void PlanPayments(SeedInvoice invoice, DateOnly day)
    {
        var left = invoice.Total - invoice.Paid;
        var roll = Random.NextDouble();
        if (left <= 0)
        {
            return;
        }

        if (roll < 0.72)
        {
            invoice.Payments.Add((day.AddDays(Random.Next(7, 41)), left));
        }
        else if (roll < 0.85)
        {
            var first = decimal.Round(left * Random.Amount(0.4m, 0.6m, 0.05m), 0);
            if (first <= 0)
            {
                return;
            }

            invoice.Payments.Add((day.AddDays(Random.Next(10, 31)), first));
            if (Random.Chance(0.5) && left - first > 0)
            {
                invoice.Payments.Add((day.AddDays(Random.Next(35, 61)), left - first));
            }
        }
    }

    private async Task ReceivePaymentsAsync(DateOnly day)
    {
        foreach (var invoice in world.Invoices)
        {
            foreach (var (_, amount) in invoice.Payments.Where(p => p.On == day))
            {
                await RecordPaymentAsync(invoice, amount, day);
            }
        }
    }

    /// <summary>
    /// In USD, except some part payments, which come in Lebanese pounds at the day's rate, rounded down to whole
    /// pounds so at the rate they never come to more than the invoice has left.
    /// </summary>
    private async Task RecordPaymentAsync(SeedInvoice invoice, decimal amountUsd, DateOnly day)
    {
        var inPounds = amountUsd < invoice.Total - invoice.Paid && Random.Chance(0.3);
        var (currency, rate) = inPounds ? ("LBP", 89500m) : ("USD", 1m);
        var amount = inPounds ? decimal.Floor(amountUsd * rate) : amountUsd;
        await api.PostAsync(Invoicer(invoice.Project, day).Token, $"/api/projects/{invoice.Project.Id}/payments", new RecordPaymentRequest
        {
            Amount = amount,
            Currency = currency,
            FxRate = currency == "USD" ? null : rate,
            ReceivedOn = day,
            Method = Random.Pick(new[] { PaymentMethod.Bank, PaymentMethod.Bank, PaymentMethod.Bank, PaymentMethod.Cheque, PaymentMethod.Cash }),
            Reference = $"LT-PAY-{++_references:00000}",
            InvoiceId = invoice.Id,
        });
        invoice.Paid += amountUsd;
        world.Tally.Add("payments");
    }

    /// <summary>Auto invoice: three monthly invoices the scheduled checks issue and email to the client on their days.</summary>
    private async Task PlanInvoicesAsync(SeedProject project, DateOnly day)
    {
        var part = decimal.Round(project.Revenue / 3m, 0);
        await api.PostAsync(Invoicer(project, day).Token, $"/api/projects/{project.Id}/invoice-plan", new InvoicePlanRequest
        {
            Invoices = Enumerable.Range(1, 3).Select(i => new PlannedInvoiceRequest
            {
                SendOn = day.AddMonths(i), Amount = i < 3 ? part : project.Revenue - 2 * part, Description = $"LT instalment {i} of 3",
            }).ToList(),
            SendTo = project.Client!.Email,
        });
        project.AutoInvoiced = true;
        project.Invoiced = project.Revenue;
        world.Tally.Add("invoices", 3);
    }

    /// <summary>The project's owner, or another admin when the owner may not void, or is gone.</summary>
    private Person Invoicer(SeedProject project, DateOnly day)
        => project.Owner.WorksOn(day) && project.Owner.Joker is null ? project.Owner
            : Random.Pick(world.Working(day, p => p.IsAdmin && p.Domain == project.Domain && p.Joker is null));
}
