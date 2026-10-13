using Kira.Application.Features.Accounting;
using Kira.Application.Features.Banking;
using Kira.Application.Features.Companies;
using Kira.Application.Features.Compliance;
using Kira.Application.Features.Documents;
using Kira.Application.Features.Insights;
using Kira.Application.Features.Members;
using Microsoft.Extensions.Options;

namespace Kira.Api.Controllers;

// Every company endpoint checks membership in the service layer (AccessService), so a user can only reach companies they belong to.

[ApiController, Route("api/v1/companies/{companyId:guid}")]
public sealed class CompaniesController(CompanyService companies) : ControllerBase
{
    [HttpGet]
    public Task<CompanyDto> Get(Guid companyId, CancellationToken ct) => companies.GetAsync(companyId, ct);

    [HttpPut]
    public Task<CompanyDto> Update(Guid companyId, CompanyUpdate body, CancellationToken ct) => companies.UpdateAsync(companyId, body, ct);

    [HttpGet("accounts")]
    public Task<List<AccountDto>> Accounts(Guid companyId, CancellationToken ct) => companies.AccountsAsync(companyId, ct);

    [HttpPost("accounts")]
    public Task<AccountDto> AddAccount(Guid companyId, NewAccount body, CancellationToken ct) => companies.AddAccountAsync(companyId, body, ct);

    [HttpPost("bank-accounts")]
    public Task<AccountDto> AddBank(Guid companyId, BankSetup body, CancellationToken ct) => companies.AddBankAsync(companyId, body, ct);
}

[ApiController, Route("api/v1/companies/{companyId:guid}/banking")]
public sealed class BankingController(BankingService banking, IOptions<UploadOptions> uploads) : ControllerBase
{
    [HttpGet]
    public Task<BankingOverview> Overview(Guid companyId, CancellationToken ct) => banking.OverviewAsync(companyId, ct);

    [HttpGet("lines")]
    public Task<List<BankLineDto>> Lines(Guid companyId, [FromQuery] BankLineStatus status = BankLineStatus.Pending, [FromQuery] Guid? bankAccountId = null,
        [FromQuery] string? month = null, [FromQuery] string? q = null, CancellationToken ct = default) =>
        banking.LinesAsync(companyId, status, bankAccountId, month, q, ct);

    [HttpPost("import"), EnableRateLimiting(RatePolicies.Ai)]
    public async Task<ImportResult> Import(Guid companyId, [FromForm] Guid bankAccountId, IFormFile file, CancellationToken ct) =>
        await banking.ImportAsync(companyId, bankAccountId, await file.ReadAsync(uploads.Value.MaxFileMb, ct), ct);

    [HttpPut("lines/{lineId:guid}")]
    public Task<SetAccountResult> SetAccount(Guid companyId, Guid lineId, SetAccountRequest body, CancellationToken ct) => banking.SetAccountAsync(companyId, lineId, body, ct);

    [HttpPost("lines/{lineId:guid}/apply-similar")]
    public Task<CountResult> ApplySimilar(Guid companyId, Guid lineId, CancellationToken ct) => banking.ApplySimilarAsync(companyId, lineId, ct);

    [HttpPost("lines/{lineId:guid}/accept")]
    public Task<EntryNumberResult> Accept(Guid companyId, Guid lineId, CancellationToken ct) => banking.AcceptAsync(companyId, lineId, ct);

    [HttpPost("lines/{lineId:guid}/cash")]
    public Task<EntryNumberResult> Cash(Guid companyId, Guid lineId, CashRequest body, CancellationToken ct) => banking.ExplainCashAsync(companyId, lineId, body, ct);

    [HttpPost("lines/{lineId:guid}/ignore")]
    public async Task<IActionResult> Ignore(Guid companyId, Guid lineId, CancellationToken ct)
    {
        await banking.IgnoreAsync(companyId, lineId, ct);
        return NoContent();
    }

    [HttpPost("accept-confident")]
    public Task<CountResult> AcceptConfident(Guid companyId, [FromQuery] decimal min = 0.9m, CancellationToken ct = default) =>
        banking.AcceptConfidentAsync(companyId, Math.Clamp(min, 0.5m, 1m), ct);

    [HttpPost("recheck"), EnableRateLimiting(RatePolicies.Ai)]
    public Task<RecheckSummary> Recheck(Guid companyId, CancellationToken ct) => banking.RecheckAsync(companyId, ct);

    [HttpGet("rules")]
    public Task<List<RuleDto>> Rules(Guid companyId, CancellationToken ct) => banking.RulesAsync(companyId, ct);

    [HttpDelete("rules/{ruleId:guid}")]
    public async Task<IActionResult> DeleteRule(Guid companyId, Guid ruleId, CancellationToken ct)
    {
        await banking.DeleteRuleAsync(companyId, ruleId, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/companies/{companyId:guid}/journal")]
public sealed class JournalController(CompanyInsightService insight) : ControllerBase
{
    [HttpGet]
    public Task<List<EntrySummary>> List(Guid companyId, [FromQuery] string? q = null, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int take = 300, CancellationToken ct = default) => insight.JournalAsync(companyId, q, from, to, take, ct);

    [HttpGet("{id:guid}")]
    public Task<EntryDetail> Get(Guid companyId, Guid id, CancellationToken ct) => insight.EntryAsync(companyId, id, ct);

    [HttpPost]
    public Task<EntryNumberResult> Create(Guid companyId, ManualEntry body, CancellationToken ct) => insight.PostManualAsync(companyId, body, ct);

    [HttpPost("{id:guid}/reverse")]
    public Task<EntryNumberResult> Reverse(Guid companyId, Guid id, CancellationToken ct) => insight.ReverseAsync(companyId, id, ct);

    [HttpPost("petty-cash")]
    public Task<EntryNumberResult> PettyCash(Guid companyId, PettyCashSpend body, CancellationToken ct) => insight.PettyCashAsync(companyId, body, ct);
}

[ApiController, Route("api/v1/companies/{companyId:guid}/reports")]
public sealed class ReportsController(ReportService reports, CompanyInsightService insight, CompanyService companies, IExcelWriter excel, AuditService audit,
    IAppDbContext db) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard(Guid companyId, CancellationToken ct) => insight.DashboardAsync(companyId, ct);

    [HttpGet("pl")]
    public Task<PnlDto> Pl(Guid companyId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => reports.PnlAsync(companyId, from, to, ct);

    [HttpGet("bs")]
    public Task<BalanceSheetDto> Bs(Guid companyId, [FromQuery] DateOnly to, CancellationToken ct) => reports.BalanceSheetAsync(companyId, to, ct);

    [HttpGet("tb")]
    public Task<TrialBalanceDto> Tb(Guid companyId, [FromQuery] DateOnly to, CancellationToken ct) => reports.TrialBalanceAsync(companyId, to, ct);

    [HttpGet("gl")]
    public Task<GeneralLedgerDto> Gl(Guid companyId, [FromQuery] Guid accountId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        reports.GeneralLedgerAsync(companyId, accountId, from, to, ct);

    /// <summary>Downloads a report as a formatted Excel workbook. kind: pl, bs, tb or gl.</summary>
    [HttpGet("export/{kind}")]
    public async Task<FileContentResult> Export(Guid companyId, string kind, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? accountId, CancellationToken ct)
    {
        await companies.GetAsync(companyId, ct);
        var c = await db.Companies.FindAsync([companyId], ct) ?? throw new NotFoundException();
        var sheet = kind switch
        {
            "pl" => ReportSheets.Pnl(c, await reports.PnlAsync(companyId, from, to, ct)),
            "bs" => ReportSheets.BalanceSheet(c, await reports.BalanceSheetAsync(companyId, to, ct)),
            "tb" => ReportSheets.TrialBalance(c, await reports.TrialBalanceAsync(companyId, to, ct)),
            "gl" => accountId is Guid a ? ReportSheets.GeneralLedger(c, await reports.GeneralLedgerAsync(companyId, a, from, to, ct)) : throw new DomainException("Choose an account."),
            _ => throw new NotFoundException("Unknown report.")
        };
        audit.Log(companyId, "Downloaded report", $"{sheet.Name} {from:dd/MM/yyyy}–{to:dd/MM/yyyy}");
        await db.SaveChangesAsync(ct);
        return File(excel.Build([sheet]), FileRules.ExcelContentType, $"{ReportSheets.SafeName(c.Name)}-{sheet.Name.Replace(' ', '-')}-{to:yyyy-MM-dd}.xlsx");
    }
}

[ApiController, Route("api/v1/companies/{companyId:guid}")]
public sealed class ComplianceController(ComplianceService compliance) : ControllerBase
{
    [HttpGet("compliance")]
    public Task<ComplianceSummary> Summary(Guid companyId, CancellationToken ct) => compliance.SummaryAsync(companyId, ct);

    [HttpGet("compliance/package")]
    public async Task<FileContentResult> Package(Guid companyId, CancellationToken ct) => (await compliance.AccountantPackageAsync(companyId, ct)).ToFile();

    [HttpPost("reminders")]
    public async Task<object> AddReminder(Guid companyId, ReminderInput body, CancellationToken ct) => new { id = await compliance.AddReminderAsync(companyId, body, ct) };

    [HttpPost("reminders/{id:guid}/done")]
    public Task<ReminderDone> Done(Guid companyId, Guid id, CancellationToken ct) => compliance.CompleteReminderAsync(companyId, id, ct);

    [HttpDelete("reminders/{id:guid}")]
    public async Task<IActionResult> DeleteReminder(Guid companyId, Guid id, CancellationToken ct)
    {
        await compliance.DeleteReminderAsync(companyId, id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/companies/{companyId:guid}/documents")]
public sealed class DocumentsController(DocumentService documents, IOptions<UploadOptions> uploads) : ControllerBase
{
    [HttpGet]
    public Task<List<DocumentDto>> List(Guid companyId, [FromQuery] Guid? bankLineId, [FromQuery] Guid? journalEntryId, CancellationToken ct) =>
        documents.ListAsync(companyId, bankLineId, journalEntryId, ct);

    [HttpPost]
    public async Task<DocumentDto> Upload(Guid companyId, IFormFile file, [FromForm] Guid? bankLineId, [FromForm] Guid? journalEntryId, [FromForm] string? note, CancellationToken ct) =>
        await documents.UploadAsync(companyId, await file.ReadAsync(uploads.Value.MaxFileMb, ct), bankLineId, journalEntryId, note, ct);

    [HttpGet("{id:guid}/file")]
    public async Task<FileContentResult> Download(Guid companyId, Guid id, CancellationToken ct) => (await documents.DownloadAsync(companyId, id, ct)).ToFile();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid companyId, Guid id, CancellationToken ct)
    {
        await documents.DeleteAsync(companyId, id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/companies/{companyId:guid}/members")]
public sealed class MembersController(MemberService members) : ControllerBase
{
    [HttpGet]
    public Task<List<MemberDto>> List(Guid companyId, CancellationToken ct) => members.ListAsync(companyId, ct);

    [HttpPost]
    public Task<AddAccountantResult> Add(Guid companyId, AddAccountantRequest body, CancellationToken ct) => members.AddAccountantAsync(companyId, body, ct);

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid companyId, Guid userId, CancellationToken ct)
    {
        await members.RemoveAsync(companyId, userId, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/companies/{companyId:guid}")]
public sealed class CompanyInsightController(CompanyInsightService insight) : ControllerBase
{
    [HttpGet("activity")]
    public Task<List<AuditDto>> Activity(Guid companyId, [FromQuery] int take = 300, CancellationToken ct = default) => insight.ActivityAsync(companyId, take, ct);

    [HttpGet("search")]
    public Task<List<SearchHit>> Search(Guid companyId, [FromQuery] string? q, CancellationToken ct) => insight.SearchAsync(companyId, q, ct);

    [HttpPost("ask"), EnableRateLimiting(RatePolicies.Ai)]
    public Task<AskResponse> Ask(Guid companyId, AskRequest body, CancellationToken ct) => insight.AskAsync(companyId, body, ct);
}
