namespace Kira.Domain.Rules;

/// <summary>
/// Malaysian tax and compliance rules, versioned by year of assessment and loaded from configuration
/// ("Rules" section of appsettings.json) so they can change without a code release.
/// </summary>
public class KiraRules
{
    public const string Section = "Rules";

    public List<TaxRule> Tax { get; set; } = [];
    public ComplianceRules Compliance { get; set; } = new();
    public List<ReliefYear> Reliefs { get; set; } = [];

    public TaxRule TaxFor(int year) =>
        Tax.Where(t => t.YearOfAssessment <= year).OrderByDescending(t => t.YearOfAssessment).FirstOrDefault()
        ?? Tax.OrderBy(t => t.YearOfAssessment).FirstOrDefault()
        ?? TaxRule.Default(year);

    public ReliefYear ReliefsFor(int year) =>
        Reliefs.Where(r => r.YearOfAssessment <= year).OrderByDescending(r => r.YearOfAssessment).FirstOrDefault()
        ?? new ReliefYear { YearOfAssessment = year };

    public AuditPhase? AuditPhaseFor(int year) =>
        Compliance.AuditExemption.Where(p => p.FromYear <= year).OrderByDescending(p => p.FromYear).FirstOrDefault();
}

public class TaxRule
{
    public int YearOfAssessment { get; set; }
    public List<TaxTier> SmeTiers { get; set; } = [];
    public decimal StandardRate { get; set; } = 24;
    public decimal FirstYearCapitalAllowancePct { get; set; } = 40;
    public decimal EntertainmentDeductiblePct { get; set; } = 50;

    public static TaxRule Default(int year) => new()
    {
        YearOfAssessment = year,
        SmeTiers = [new() { UpTo = 150000, Rate = 15 }, new() { UpTo = 600000, Rate = 17 }, new() { UpTo = null, Rate = 24 }]
    };
}

public class TaxTier
{
    public decimal? UpTo { get; set; }
    public decimal Rate { get; set; }
}

public class ComplianceRules
{
    public decimal EInvoiceExemptBelow { get; set; } = 3_000_000;
    public string EInvoiceNote { get; set; } = "";
    public List<AuditPhase> AuditExemption { get; set; } = [];
    public int FormCMonthsAfterYearEnd { get; set; } = 7;
    public decimal Cp204PenaltyMarginPct { get; set; } = 30;
    /// <summary>Months of the financial year in which CP204 can be revised. Empty means 6, 9 and 11.</summary>
    public List<int> Cp204RevisionMonths { get; set; } = [];
    public string PersonalFilingNote { get; set; } = "";
}

public class AuditPhase
{
    public int FromYear { get; set; }
    public decimal Revenue { get; set; }
    public decimal Assets { get; set; }
    public int Employees { get; set; }
}

public class ReliefYear
{
    public int YearOfAssessment { get; set; }
    public List<ReliefItem> Items { get; set; } = [];
}

public class ReliefItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Limit { get; set; }
    public string Hint { get; set; } = "";
    public bool New { get; set; }
}
