using Kira.Application.Features.Insights;
using Kira.Application.Features.Personal;
using Microsoft.Extensions.Options;

namespace Kira.Api.Controllers.Personal;

// Personal endpoints never take a company id. Every service call is scoped to the signed-in user.

[ApiController, Route("api/v1/personal/accounts")]
public sealed class PaymentAccountsController(PaymentAccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<List<PaymentAccountDto>> List(CancellationToken ct) => accounts.ListAsync(ct);

    [HttpPost]
    public Task<PaymentAccountDto> Add(PaymentAccountInput body, CancellationToken ct) => accounts.AddAsync(body, ct);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PaymentAccountInput body, CancellationToken ct)
    {
        await accounts.UpdateAsync(id, body, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await accounts.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/personal/transactions")]
public sealed class PersonalTransactionsController(PersonalTransactionService tx, IOptions<UploadOptions> uploads) : ControllerBase
{
    [HttpGet]
    public Task<List<PTxDto>> List([FromQuery] Guid? accountId, [FromQuery] string? month, [FromQuery] bool review = false, [FromQuery] string? q = null, CancellationToken ct = default) =>
        tx.ListAsync(accountId, month, review, q, ct);

    [HttpPost]
    public async Task<object> Add(PTxInput body, CancellationToken ct) => new { id = await tx.AddAsync(body, ct) };

    [HttpPut("{id:guid}")]
    public Task<SimilarResult> Update(Guid id, PTxUpdate body, CancellationToken ct) => tx.UpdateAsync(id, body, ct);

    [HttpPost("{id:guid}/apply-similar")]
    public async Task<object> ApplySimilar(Guid id, CancellationToken ct) => new { updated = await tx.ApplySimilarAsync(id, ct) };

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        await tx.ConfirmAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tx.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("import"), EnableRateLimiting(RatePolicies.Ai)]
    public async Task<PImportResult> Import([FromForm] Guid accountId, IFormFile file, CancellationToken ct) =>
        await tx.ImportAsync(accountId, await file.ReadAsync(uploads.Value.MaxFileMb, ct), ct);
}

[ApiController, Route("api/v1/personal/receipts")]
public sealed class ReceiptsController(ReceiptService receipts, IOptions<UploadOptions> uploads) : ControllerBase
{
    [HttpGet]
    public Task<List<ReceiptDto>> List([FromQuery] bool needsAttention = false, CancellationToken ct = default) => receipts.ListAsync(needsAttention, ct);

    [HttpGet("{id:guid}")]
    public Task<ReceiptDto> Get(Guid id, CancellationToken ct) => receipts.GetAsync(id, ct);

    [HttpGet("{id:guid}/image")]
    public async Task<FileContentResult> Image(Guid id, CancellationToken ct) => (await receipts.FileAsync(id, false, ct)).ToFile();

    [HttpGet("{id:guid}/screenshot")]
    public async Task<FileContentResult> Screenshot(Guid id, CancellationToken ct) => (await receipts.FileAsync(id, true, ct)).ToFile();

    [HttpPost, EnableRateLimiting(RatePolicies.Ai)]
    public async Task<ReceiptDto> Upload(IFormFile file, CancellationToken ct) => await receipts.UploadAsync(await file.ReadAsync(uploads.Value.MaxFileMb, ct), ct);

    [HttpPost("manual")]
    public Task<ReceiptDto> Manual(CancellationToken ct) => receipts.CreateManualAsync(ct);

    [HttpPut("{id:guid}")]
    public Task<ReceiptDto> Update(Guid id, ReceiptUpdate body, CancellationToken ct) => receipts.UpdateAsync(id, body, ct);

    [HttpPut("{id:guid}/payment")]
    public Task<ReceiptDto> Payment(Guid id, PaymentUpdate body, CancellationToken ct) => receipts.SetPaymentAsync(id, body, ct);

    [HttpPost("{id:guid}/screenshot"), EnableRateLimiting(RatePolicies.Ai)]
    public async Task<ReceiptDto> UploadScreenshot(Guid id, IFormFile file, CancellationToken ct) =>
        await receipts.UploadScreenshotAsync(id, await file.ReadAsync(uploads.Value.MaxFileMb, ct), ct);

    [HttpPut("{id:guid}/relief")]
    public Task<ReceiptDto> Relief(Guid id, ReliefUpdate body, CancellationToken ct) => receipts.SetReliefAsync(id, body, ct);

    [HttpPost("{id:guid}/save")]
    public Task<ReceiptDto> Save(Guid id, CancellationToken ct) => receipts.SaveAsync(id, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await receipts.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/v1/personal")]
public sealed class PersonalInsightController(PersonalInsightService insight) : ControllerBase
{
    [HttpGet("overview")]
    public Task<PersonalOverview> Overview([FromQuery] string? month, CancellationToken ct) => insight.OverviewAsync(month, ct);

    [HttpGet("reliefs")]
    public Task<ReliefSummary> Reliefs([FromQuery] int? year, CancellationToken ct) => insight.ReliefsAsync(year, ct);

    [HttpGet("reports/spending")]
    public Task<SpendingReport> Spending([FromQuery] int? year, CancellationToken ct) => insight.SpendingAsync(year, ct);

    [HttpGet("reports/spending/export")]
    public async Task<FileContentResult> SpendingExport([FromQuery] int? year, CancellationToken ct) => (await insight.SpendingExportAsync(year, ct)).ToFile();

    [HttpGet("search")]
    public Task<List<SearchHit>> Search([FromQuery] string? q, CancellationToken ct) => insight.SearchAsync(q, ct);

    [HttpPost("ask"), EnableRateLimiting(RatePolicies.Ai)]
    public Task<AskResponse> Ask(AskRequest body, CancellationToken ct) => insight.AskAsync(body, ct);
}
