using Kira.Application.Common;
using Kira.Application.Features.Companies;
using Kira.Domain.Common;
using Kira.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kira.Infrastructure.Persistence;

/// <summary>
/// Demo data so the app is explorable on first run (only when the database has no users and Seed:Demo is true).
/// Logins: hassan@demo.kira (owner, with a personal workspace) and sarah@demo.kira (tax agent, read-only). Password: Demo12345!
/// </summary>
public sealed class DemoSeeder(KiraDbContext db, IPasswordHasher hasher, IClock clock, CompanyService companies)
{
    public const string Password = "Demo12345!";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var today = clock.Today;
        var year = today.Year;

        var hassan = new User { Email = "hassan@demo.kira", Name = "Hassan Rahman", Title = "Director, ABC Software", PasswordHash = hasher.Hash(Password) };
        var sarah = new User { Email = "sarah@demo.kira", Name = "Sarah Lim", Title = "Tax agent, Lim & Co", UsesPersonal = false, PasswordHash = hasher.Hash(Password) };
        db.Users.AddRange(hassan, sarah);
        var c = companies.Create(hassan.Id, new CompanySetup("ABC Software Sdn. Bhd.", "202301012345 (1501234-X)", new DateOnly(2023, 1, 10), 1,
            "software development", [new BankSetup("Maybank Business Current", "4471"), new BankSetup("CIMB Business Current", "0045")]));
        c.Cp204Estimate = 21600; c.EmployeeCount = 2; c.Address = "Putra Heights, Selangor";
        db.Memberships.Add(new Membership { CompanyId = c.Id, UserId = sarah.Id, Role = MemberRole.Accountant });
        await db.SaveChangesAsync(ct);

        var acc = await db.Accounts.Where(a => a.CompanyId == c.Id).ToDictionaryAsync(a => a.Code, a => a.Id, ct);
        var number = 0;
        long seed = 11;
        decimal Rnd() { seed = seed * 16807 % 2147483647; return seed / 2147483647m; }
        DateOnly D(int m, int d) => new(year, m, Math.Min(d, DateTime.DaysInMonth(year, m)));
        void Post(DateOnly date, string memo, string dr, string cr, decimal amount, string source = "bank") =>
            db.JournalEntries.Add(JournalEntry.Create(c.Id, ++number, date, memo, source, hassan.Id,
                [new LineInput(acc[dr], Math.Round(amount, 2), 0), new LineInput(acc[cr], 0, Math.Round(amount, 2))]));

        db.JournalEntries.Add(JournalEntry.Create(c.Id, ++number, new DateOnly(year, 1, 1), "Opening balances brought forward", "opening", hassan.Id,
        [
            new LineInput(acc["1000"], 86000, 0), new LineInput(acc["1010"], 12000, 0), new LineInput(acc["1200"], 18000, 0),
            new LineInput(acc["3000"], 0, 50000), new LineInput(acc["3100"], 0, 66000)
        ]));

        var lastFull = Math.Clamp(today.Month - 1, 1, 11);
        for (var m = 1; m <= lastFull; m++)
        {
            var a1 = Math.Round(17000 + Rnd() * 7000); var a2 = Math.Round(6000 + Rnd() * 5000);
            Post(D(m, 3), $"Invoice INV-{1000 + m * 2 - 1}, Nexus Retail Sdn Bhd", "1100", "4000", a1, "invoice");
            Post(D(m, 6), $"Invoice INV-{1000 + m * 2}, Kenanga Logistics Bhd", "1100", "4000", a2, "invoice");
            if (m < lastFull) Post(D(m, 20), $"DUITNOW CR NEXUS RETAIL INV-{1000 + m * 2 - 1}", "1000", "1100", a1);
            if (m < lastFull - 1) Post(D(m, 22), $"IBG CR KENANGA LOGISTICS INV-{1000 + m * 2}", "1000", "1100", a2);
            Post(D(m, 1), "IBG RENT MAJESTIC PROPERTIES PUTRA HEIGHTS", "6100", "1000", 4500);
            Post(D(m, 5), "GOOGLE WORKSPACE / AWS", "6120", "1000", 1100 + Math.Round(Rnd() * 200));
            Post(D(m, 10), "PEMBAYARAN JOMPAY TNB / UNIFI", "6110", "1000", 480 + Rnd() * 190);
            Post(D(m, 12), "FPX META PLATFORMS IRELAND LTD", "6140", "1000", 700 + Math.Round(Rnd() * 1600));
            Post(D(m, 14), "KWSP / PERKESO / EIS", "6010", "1000", 1310);
            Post(D(m, 15), $"BAYARAN LHDN CP204 ANSURAN {m:00}", "1500", "1000", 1800);
            Post(D(m, 18), "GRAB / TOUCH N GO / PARKING", "6150", "1000", 150 + Rnd() * 420);
            if (m % 2 == 0) Post(D(m, 19), "Client lunch", "6160", "1000", 250 + Rnd() * 500);
            Post(D(m, 25), "IBG SALARY", "6000", "1000", 9200);
            Post(D(m, 28), "SERVICE CHARGE", "6180", "1000", 14 + Rnd() * 20);
        }
        if (lastFull >= 2) Post(D(2, 14), "MacBook Pro 14 (Machines Sdn Bhd)", "1200", "1000", 8000);
        if (lastFull >= 3) Post(D(3, 20), $"Audit and tax agent fee FY{year - 1}", "6130", "1000", 6500);
        if (lastFull >= 6) Post(D(6, 15), "Office furniture, Lavino", "1210", "1000", 12000);
        if (lastFull >= 7) Post(D(7, 8), "Transfer to director (personal)", "1400", "1000", 3000);

        // Statement lines from the latest month, waiting for review.
        void Line(int d, string desc, Direction dir, decimal amt, string? code, decimal conf, string why, string flags = "") =>
            db.BankLines.Add(new BankLine
            {
                CompanyId = c.Id, BankAccountId = acc["1000"], Date = D(lastFull, d), Description = desc, Direction = dir, Amount = amt,
                SuggestedAccountId = code == null ? null : acc[code], Confidence = conf, Reason = why, Flags = flags, ImportBatch = "demo", SourceFile = "Demo statement"
            });
        Line(26, "DUITNOW CR NEXUS RETAIL SDN BHD INV-1017", Direction.In, 21450, "1100", 0.93m, "Matches an open customer invoice");
        Line(27, "ATM CASH WDL PUTRA HEIGHTS", Direction.Out, 500, null, 0, "Cash left the bank. Petty cash or director drawings?", LineFlags.CashWithdrawal);
        Line(28, "IBG TRF TO AHMAD ZAKI BIN ISMAIL", Direction.Out, 1500, "5000", 0.45m, "Transfer to a person, purpose not stated");
        Line(28, "FPX META PLATFORMS IRELAND LTD", Direction.Out, 1286.40m, "6140", 0.96m, "Facebook and Instagram ads", LineFlags.ForeignPayee);
        Line(29, "PEMBAYARAN JOMPAY TNB 2209123", Direction.Out, 612.40m, "6110", 0.97m, "Electricity bill, office");
        Line(30, "SERVICE CHARGE", Direction.Out, 12, "6180", 0.99m, "Monthly bank charge");
        Line(30, "HIBAH", Direction.In, 6.12m, "4100", 0.94m, "Islamic account profit");
        db.Reminders.Add(new Reminder { CompanyId = c.Id, Name = "Business premise licence (MBSJ)", DueDate = today.AddDays(12), Repeat = ReminderRepeat.Yearly });
        db.Reminders.Add(new Reminder { CompanyId = c.Id, Name = "Office insurance renewal", DueDate = today.AddDays(25), Repeat = ReminderRepeat.Yearly });
        db.CategoryRules.Add(new CategoryRule { CompanyId = c.Id, Pattern = "tnb", Value = "6110" });
        db.CategoryRules.Add(new CategoryRule { CompanyId = c.Id, Pattern = "kwsp perkeso eis", Value = "6010" });

        // Hassan's personal workspace
        var u = hassan.Id;
        var maybank = new PaymentAccount { UserId = u, Name = "Maybank Savings", Kind = PaymentKind.Bank, Institution = "Maybank", Last4 = "4412", OpeningBalance = 6500, SortOrder = 1 };
        var visa = new PaymentAccount { UserId = u, Name = "CIMB Visa", Kind = PaymentKind.CreditCard, Institution = "CIMB", Last4 = "2201", SortOrder = 2 };
        var tng = new PaymentAccount { UserId = u, Name = "Touch 'n Go eWallet", Kind = PaymentKind.EWallet, Institution = "Touch 'n Go", Last4 = "4410", OpeningBalance = 30, SortOrder = 3 };
        var cash = new PaymentAccount { UserId = u, Name = "Cash", Kind = PaymentKind.Cash, OpeningBalance = 500, SortOrder = 4 };
        db.PaymentAccounts.AddRange(maybank, visa, tng, cash);
        var p0 = new DateOnly(year, lastFull, 1);
        void Tx(PaymentAccount a, int d, string desc, Direction dir, decimal amt, string cat, bool transfer = false, bool reviewed = true, string reason = "") =>
            db.PersonalTransactions.Add(new PersonalTransaction
            {
                UserId = u, PaymentAccountId = a.Id, Date = p0.AddDays(d - 1), Description = desc, Direction = dir, Amount = amt, Category = cat,
                IsTransfer = transfer, Source = TransactionSource.Statement, Reviewed = reviewed, Confidence = reviewed ? 1 : 0.4m, Reason = reason
            });
        Tx(maybank, 25, "SALARY ABC SOFTWARE SDN BHD", Direction.In, 12000, "Income");
        Tx(maybank, 1, "HOUSING LOAN MAYBANK", Direction.Out, 2650, "Housing");
        Tx(maybank, 10, "JOMPAY TNB", Direction.Out, 168.20m, "Utilities");
        Tx(maybank, 11, "TNG EWALLET RELOAD", Direction.Out, 100, PersonalCategories.Transfers, true);
        Tx(tng, 11, "Reload from Maybank", Direction.In, 100, PersonalCategories.Transfers, true);
        Tx(tng, 12, "GRAB RIDE", Direction.Out, 23.40m, "Transport");
        Tx(tng, 14, "PLUS TOLL", Direction.Out, 22.40m, "Transport");
        Tx(visa, 6, "TESCO STORES SETIA ALAM", Direction.Out, 214.80m, "Groceries");
        Tx(visa, 15, "NETFLIX.COM", Direction.Out, 54.90m, "Entertainment");
        Tx(visa, 12, "PETRONAS PUTRA HEIGHTS", Direction.Out, 120, "Fuel");
        Tx(maybank, 20, "CIMB VISA PAYMENT", Direction.Out, 300, PersonalCategories.Transfers, true);
        Tx(visa, 20, "PAYMENT RECEIVED THANK YOU", Direction.In, 300, PersonalCategories.Transfers, true);
        Tx(maybank, 22, "DUITNOW TO AMIR HAKIM", Direction.Out, 250, "Other", reviewed: false, reason: "Transfer to a person, purpose unclear");

        void Rc(string merchant, int d, decimal total, string cat, PaymentStatus status, PaymentAccount? paidFrom, PaymentSource source, string note,
            string printed = "", string? relief = null, string? suggested = null)
        {
            var saved = suggested == null;
            var r = new Receipt
            {
                UserId = u, Merchant = merchant, Date = p0.AddDays(d - 1), Total = total, Category = cat, Confidence = 0.95m, Reason = "Read by Kira",
                PaymentStatus = status, PaymentAccountId = paidFrom?.Id, PaymentSource = source, PaymentNote = note, PrintedPayment = printed,
                PaymentConfidence = paidFrom != null ? 0.95m : 0, ReliefCode = relief, SuggestedReliefCode = suggested ?? relief,
                ReliefConfidence = suggested != null || relief != null ? 0.75m : 0, ReliefReason = suggested != null ? "Clinic visit could count as medical relief" : "",
                Status = saved ? ReceiptStatus.Saved : ReceiptStatus.Review
            };
            db.Receipts.Add(r);
            if (saved && status != PaymentStatus.NotPaid)
            {
                var t = new PersonalTransaction
                {
                    UserId = u, PaymentAccountId = paidFrom?.Id, Date = r.Date, Description = merchant.ToUpperInvariant(), Direction = Direction.Out,
                    Amount = total, Category = cat, Source = TransactionSource.Receipt, ReceiptId = r.Id
                };
                db.PersonalTransactions.Add(t);
                r.TransactionId = t.Id;
            }
        }
        Rc("The Coffee Bean, Berjaya Times Square", 24, 104.62m, "Dining", PaymentStatus.Account, maybank, PaymentSource.Screenshot, "Savings ••4412 matches Maybank Savings", "DuitNow QR");
        Rc("AEON Big Subang Jaya", 27, 93.99m, "Groceries", PaymentStatus.Account, visa, PaymentSource.Receipt, "VISA ••2201 printed on the receipt", "VISA ••2201");
        Rc("Pasar malam Putra Heights", 28, 32, "Groceries", PaymentStatus.Cash, cash, PaymentSource.Manual, "Paid in cash");
        Rc("Popular Bookstore", 9, 89.90m, "Education", PaymentStatus.Account, visa, PaymentSource.Manual, "Chosen by you", relief: "LIFESTYLE");
        Rc("Klinik Mediviron", 17, 65, "Health", PaymentStatus.Unknown, null, PaymentSource.None, "", suggested: "MEDICAL");
        await db.SaveChangesAsync(ct);
    }
}
