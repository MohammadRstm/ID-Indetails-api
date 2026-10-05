using System.Text;
using System.Text.Json;

namespace ID_Indetails.LoadTests.Tool.Verification;

/// <summary>The database checks after a run, as verification.json (for machines) and verification.md (for people).</summary>
public sealed record VerificationReport(
    string Run,
    IReadOnlyList<CheckResult> Rules,
    IReadOnlyList<Expectation> Ledger,
    IReadOnlyList<(string Race, string Rule, bool Holds)> Races,
    IReadOnlyDictionary<string, int> Changes)
{
    public bool Passed => Rules.All(r => r.Violations == 0) && Ledger.All(e => e.Holds) && Races.All(r => r.Holds);

    public async Task WriteAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var json = new
        {
            run = Run,
            passed = Passed,
            rules = Rules.Select(r => new { rule = r.Name, violations = r.Violations }),
            ledger = Ledger.Select(e => new { check = e.What, expected = e.Expected, actual = e.Actual, holds = e.Holds }),
            races = Races.Select(r => new { race = r.Race, rule = r.Rule, holds = r.Holds }),
            changes = Changes,
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "verification.json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(folder, "verification.md"), Markdown());
    }

    public string Markdown()
    {
        var md = new StringBuilder();
        md.AppendLine($"# Database verification, run {Run}: {(Passed ? "passed" : "FAILED")}").AppendLine();
        md.AppendLine("## Rules the data must keep").AppendLine().AppendLine("| Rule | Rows breaking it |").AppendLine("|---|---|");
        foreach (var rule in Rules)
        {
            md.AppendLine($"| {rule.Name} | {(rule.Violations == 0 ? "0" : $"**{rule.Violations}**")} |");
        }

        if (Ledger.Count > 0)
        {
            md.AppendLine().AppendLine("## What the run changed, against the database").AppendLine();
            md.AppendLine("| Check | Expected | In the database | |").AppendLine("|---|---|---|---|");
            foreach (var e in Ledger)
            {
                md.AppendLine($"| {e.What} | {e.Expected} | {e.Actual} | {(e.Holds ? "ok" : "**MISMATCH**")} |");
            }
        }

        if (Races.Count > 0)
        {
            md.AppendLine().AppendLine("## After each race").AppendLine().AppendLine("| Race | Rule | |").AppendLine("|---|---|---|");
            foreach (var (race, rule, holds) in Races)
            {
                md.AppendLine($"| {race} | {rule} | {(holds ? "holds" : "**BROKEN**")} |");
            }
        }

        if (Changes.Count > 0)
        {
            md.AppendLine().AppendLine("## Changes the run made (ledger)").AppendLine().AppendLine("| Change | Count |").AppendLine("|---|---|");
            foreach (var (kind, count) in Changes.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                md.AppendLine($"| {kind} | {count} |");
            }
        }

        return md.ToString();
    }
}
