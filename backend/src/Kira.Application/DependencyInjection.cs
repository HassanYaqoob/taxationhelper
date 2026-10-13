using Kira.Application.Ai;
using Kira.Application.Features.Accounting;
using Kira.Application.Features.Auth;
using Kira.Application.Features.Banking;
using Kira.Application.Features.Companies;
using Kira.Application.Features.Compliance;
using Kira.Application.Features.Documents;
using Kira.Application.Features.Insights;
using Kira.Application.Features.Members;
using Kira.Application.Features.Personal;
using Microsoft.Extensions.DependencyInjection;

namespace Kira.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AccessService>();
        services.AddScoped<AuditService>();
        services.AddScoped<RuleService>();
        services.AddScoped<FileStore>();
        services.AddScoped<Extractors>();

        services.AddScoped<AuthService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<AccountingService>();
        services.AddScoped<ReportService>();
        services.AddScoped<BankingService>();
        services.AddScoped<ComplianceService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<MemberService>();
        services.AddScoped<CompanyInsightService>();
        services.AddScoped<PaymentAccountService>();
        services.AddScoped<PersonalTransactionService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<PersonalInsightService>();
        return services;
    }
}
