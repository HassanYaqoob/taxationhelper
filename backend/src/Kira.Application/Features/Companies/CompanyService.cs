using Kira.Application.Features.Accounting;
using Kira.Application.Features.Auth;

namespace Kira.Application.Features.Companies;

public sealed record BankSetup(string Name, string? Last4);
public sealed record CompanySetup(string Name, string? RegistrationNo, DateOnly? IncorporatedOn, int FyStartMonth, string? Industry, List<BankSetup>? Banks);
public sealed record CompanyDto(Guid Id, string Name, string RegistrationNo, DateOnly? IncorporatedOn, string Industry, string Address, int FyStartMonth,
    bool IsSme, decimal Cp204Estimate, int EmployeeCount, string Role);
public sealed record CompanyUpdate(string Name, string? RegistrationNo, DateOnly? IncorporatedOn, string? Industry, string? Address, int FyStartMonth,
    bool IsSme, decimal Cp204Estimate, int EmployeeCount);
public sealed record AccountDto(Guid Id, string Code, string Name, AccountType Type, TaxTreatment Tax, string? SystemKey, string Hint, string Last4, decimal Balance);
public sealed record NewAccount(string Code, string Name, AccountType Type, TaxTreatment Tax, string? Hint);

public sealed class CompanyService(IAppDbContext db, AccessService access, AuditService audit, ICurrentUser me, ReportService reports, IClock clock)
{
    /// <summary>Creates a company with the default chart of accounts, its bank accounts and the owner's membership. Caller saves.</summary>
    public Company Create(Guid ownerId, CompanySetup s)
    {
        if (string.IsNullOrWhiteSpace(s.Name)) throw new DomainException("Enter the company name.");
        var c = new Company
        {
            Name = s.Name.Trim(), RegistrationNo = s.RegistrationNo?.Trim() ?? "", IncorporatedOn = s.IncorporatedOn,
            FyStartMonth = s.FyStartMonth is >= 1 and <= 12 ? s.FyStartMonth : 1, Industry = s.Industry?.Trim() ?? ""
        };
        db.Companies.Add(c);
        db.Memberships.Add(new Membership { CompanyId = c.Id, UserId = ownerId, Role = MemberRole.Owner });
        var banks = (s.Banks ?? []).Where(b => !string.IsNullOrWhiteSpace(b.Name)).ToList();
        if (banks.Count == 0) banks.Add(new BankSetup("Business current account", ""));
        for (var i = 0; i < banks.Count; i++)
            db.Accounts.Add(new Account
            {
                CompanyId = c.Id, Code = (1000 + i * 10).ToString(), Name = banks[i].Name.Trim(), Type = AccountType.Asset,
                Tax = TaxTreatment.Bank, Last4 = PaymentMatcher.Digits(banks[i].Last4)
            });
        foreach (var it in DefaultChart.Accounts)
            db.Accounts.Add(new Account { CompanyId = c.Id, Code = it.Code, Name = it.Name, Type = it.Type, Tax = it.Tax, SystemKey = it.SystemKey, Hint = it.Hint });
        return c;
    }

    public async Task<WorkspaceDto> CreateForMeAsync(CompanySetup setup, CancellationToken ct)
    {
        var c = Create(me.Id, setup);
        await db.SaveChangesAsync(ct);
        return new WorkspaceDto("company", c.Id, c.Name, MemberRole.Owner.ToString());
    }

    public async Task<CompanyDto> GetAsync(Guid companyId, CancellationToken ct)
    {
        var m = await access.ViewAsync(companyId, ct);
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId, ct) ?? throw new NotFoundException();
        return ToDto(c, m.Role);
    }

    public async Task<CompanyDto> UpdateAsync(Guid companyId, CompanyUpdate u, CancellationToken ct)
    {
        var m = await access.EditAsync(companyId, ct);
        var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == companyId, ct) ?? throw new NotFoundException();
        if (string.IsNullOrWhiteSpace(u.Name)) throw new DomainException("Company name can't be empty.");
        c.Name = u.Name.Trim(); c.RegistrationNo = u.RegistrationNo?.Trim() ?? ""; c.IncorporatedOn = u.IncorporatedOn;
        c.Industry = u.Industry?.Trim() ?? ""; c.Address = u.Address?.Trim() ?? "";
        c.FyStartMonth = u.FyStartMonth is >= 1 and <= 12 ? u.FyStartMonth : c.FyStartMonth;
        c.IsSme = u.IsSme; c.Cp204Estimate = Math.Max(0, u.Cp204Estimate); c.EmployeeCount = Math.Max(0, u.EmployeeCount);
        audit.Log(companyId, "Updated company settings", c.Name);
        await db.SaveChangesAsync(ct);
        return ToDto(c, m.Role);
    }

    public async Task<List<AccountDto>> AccountsAsync(Guid companyId, CancellationToken ct)
    {
        await access.ViewAsync(companyId, ct);
        var ledger = await reports.LedgerAsync(companyId, null, clock.Today, ct);
        return ledger.Accounts.Select(a => ToDto(a, ledger.Balance(a))).ToList();
    }

    public async Task<AccountDto> AddAccountAsync(Guid companyId, NewAccount n, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        var code = (n.Code ?? "").Trim();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(n.Name)) throw new DomainException("Enter a code and a name.");
        if (n.Tax == TaxTreatment.Bank) throw new DomainException("Add bank accounts under Bank accounts.");
        if (await db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.Code == code, ct)) throw new DomainException("That account code is already used.");
        var a = new Account { CompanyId = companyId, Code = code, Name = n.Name.Trim(), Type = n.Type, Tax = n.Tax, Hint = n.Hint?.Trim() ?? "" };
        db.Accounts.Add(a);
        audit.Log(companyId, "Added account", $"{a.Code} {a.Name}");
        await db.SaveChangesAsync(ct);
        return ToDto(a, 0);
    }

    public async Task<AccountDto> AddBankAsync(Guid companyId, BankSetup b, CancellationToken ct)
    {
        await access.EditAsync(companyId, ct);
        if (string.IsNullOrWhiteSpace(b.Name)) throw new DomainException("Enter the bank account name.");
        var codes = await db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.Code).ToListAsync(ct);
        var code = 1000;
        while (codes.Contains(code.ToString())) code += code < 1040 ? 10 : 1;
        var a = new Account
        {
            CompanyId = companyId, Code = code.ToString(), Name = b.Name.Trim(), Type = AccountType.Asset, Tax = TaxTreatment.Bank,
            Last4 = PaymentMatcher.Digits(b.Last4)
        };
        db.Accounts.Add(a);
        audit.Log(companyId, "Added bank account", a.Name);
        await db.SaveChangesAsync(ct);
        return ToDto(a, 0);
    }

    /// <summary>The categories list the AI chooses from, one account per line.</summary>
    public static string CategoryList(IEnumerable<Account> accounts, Guid? excludeBank) =>
        string.Join("\n", accounts.Where(a => a.Id != excludeBank).OrderBy(a => a.Code).Select(a =>
            $"- {a.Code}: {a.Name}" + (a.IsBank ? " (transfer to or from this company's own bank account)" : "") +
            (string.IsNullOrEmpty(a.Hint) ? "" : $". {a.Hint}")));

    static AccountDto ToDto(Account a, decimal balance) => new(a.Id, a.Code, a.Name, a.Type, a.Tax, a.SystemKey, a.Hint, a.Last4, balance);
    static CompanyDto ToDto(Company c, MemberRole role) => new(c.Id, c.Name, c.RegistrationNo, c.IncorporatedOn, c.Industry, c.Address, c.FyStartMonth,
        c.IsSme, c.Cp204Estimate, c.EmployeeCount, role.ToString());
}
