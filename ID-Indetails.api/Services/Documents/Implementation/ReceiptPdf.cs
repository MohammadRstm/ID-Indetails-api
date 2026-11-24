using System.Globalization;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Documents.Implementation;

/// <summary>
/// Where the whole project stands for its client by a payment, it included, in USD with TVA: what the project costs them
/// in all and what they had paid by then. The total is 0 while an Execution site has no agreed BOQ.
/// </summary>
public sealed record ReceiptPosition(decimal ProjectTotal, decimal PaidSoFar)
{
    public decimal StillToPay => Math.Max(0, ProjectTotal - PaidSoFar);

    public decimal PaidAhead => ProjectTotal > 0 ? Math.Max(0, PaidSoFar - ProjectTotal) : 0;
}

/// <summary>
/// The receipt the client gets for a payment: that it was received, and where their project stands. Like the invoice,
/// it reads only client-facing fields, never the payment's internal note, its account label, costs or profit.
/// </summary>
public static class ReceiptPdf
{
    /// <summary>A receipt is numbered after its payment, so it is the same each time it is made.</summary>
    public static string Number(PaymentResponse payment) => $"R-{payment.Id}";

    public static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Bank => "Bank transfer",
        PaymentMethod.Cheque => "Cheque",
        PaymentMethod.Cash => "Cash",
        _ => "Other",
    };

    /// <summary>The payment's amount in its own currency and, for another currency, what it came to in US dollars.</summary>
    public static IReadOnlyList<TotalRow> PaymentRows(PaymentResponse payment)
    {
        var rows = new List<TotalRow> { new("Amount received", PdfLayout.Money(payment.Amount, payment.Currency), Strong: true) };
        if (payment.Currency != "USD")
        {
            var rate = payment.FxRate.ToString("#,0.##", CultureInfo.InvariantCulture);
            rows.Add(new($"In US dollars, at {rate} {payment.Currency} to the dollar", PdfLayout.Money(payment.AmountUsd)));
        }

        return rows;
    }

    /// <summary>The project's total, what was paid by this payment, and what is left or paid ahead. Only what was paid while there is no total yet.</summary>
    public static IReadOnlyList<TotalRow> PositionRows(ReceiptPosition position)
    {
        var paid = new TotalRow("Paid so far, this payment included", PdfLayout.Money(position.PaidSoFar));
        if (position.ProjectTotal <= 0)
        {
            return [paid];
        }

        return
        [
            new TotalRow("Project total", PdfLayout.Money(position.ProjectTotal)),
            paid,
            position.PaidAhead > 0
                ? new TotalRow("Paid ahead", PdfLayout.Money(position.PaidAhead), Strong: true)
                : new TotalRow("Still to pay", PdfLayout.Money(position.StillToPay), Strong: true),
        ];
    }

    /// <param name="billing">The business's wording for its documents; null when it isn't set up.</param>
    public static byte[] Render(PaymentResponse payment, ReceiptPosition position, Letterhead head, BillingSettings? billing)
    {
        var number = Number(payment);
        var (document, section) = PdfLayout.NewDocument($"Receipt {number}", $"{head.CompanyName} · Receipt {number}", forClient: true);
        PdfLayout.AddLetterhead(section, head, "Receipt", number, null);

        IReadOnlyList<Fact> how = payment.Reference is { } reference
            ? [new Fact("Paid by", Method(payment.Method)), new Fact("Reference", reference)]
            : [new Fact("Paid by", Method(payment.Method))];
        PdfLayout.AddFacts(
            section,
            [new Fact("Received from", payment.Client.DisplayName)],
            [Fact.Project(payment.Project.Title, payment.Project.Code, payment.Project.Location)],
            [new Fact("Date received", PdfLayout.Date(payment.ReceivedOn)), .. how]);

        PdfLayout.AddHeading(section, payment.InvoiceNumber is { } invoice ? $"Payment against invoice {invoice}" : "Payment");
        PdfLayout.AddTotals(section, [.. PaymentRows(payment)]);

        PdfLayout.AddHeading(section, "Your project");
        PdfLayout.AddTotals(section, [.. PositionRows(position)]);

        PdfLayout.AddNote(section, "Thank you for your payment. This receipt confirms that we received it.");
        if (!string.IsNullOrWhiteSpace(billing?.Footer))
        {
            PdfLayout.AddNote(section, billing.Footer, spaceBeforePt: 18);
        }

        return PdfLayout.Render(document);
    }
}
