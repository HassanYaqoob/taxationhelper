using Kira.Domain.Common;

namespace Kira.Domain.Entities;

public class Company : Entity
{
    public string Name { get; set; } = "";
    public string RegistrationNo { get; set; } = "";
    public DateOnly? IncorporatedOn { get; set; }
    public string Industry { get; set; } = "";
    public string Address { get; set; } = "";
    public int FyStartMonth { get; set; } = 1;
    public bool IsSme { get; set; } = true;
    public decimal Cp204Estimate { get; set; }
    public int EmployeeCount { get; set; }

    public DateOnly FyStart(DateOnly date)
    {
        var year = date.Month >= FyStartMonth ? date.Year : date.Year - 1;
        return new DateOnly(year, FyStartMonth, 1);
    }

    public static DateOnly FyEnd(DateOnly fyStart) => fyStart.AddYears(1).AddDays(-1);
}

/// <summary>Links a user to a company. Personal data is never reachable through a membership.</summary>
public class Membership : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public MemberRole Role { get; set; }
}

public class Reminder : Entity
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly DueDate { get; set; }
    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Yearly;
    public string Notes { get; set; } = "";

    /// <summary>Marks the reminder done. Returns false if it was a one-off and should be removed.</summary>
    public bool Complete()
    {
        switch (Repeat)
        {
            case ReminderRepeat.Monthly: DueDate = DueDate.AddMonths(1); return true;
            case ReminderRepeat.Yearly: DueDate = DueDate.AddYears(1); return true;
            default: return false;
        }
    }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
