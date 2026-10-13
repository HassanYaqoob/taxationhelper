using Kira.Application.Ai;
using Kira.Domain.Accounting;
using Kira.Domain.Common;
using Kira.Domain.Entities;
using Kira.Domain.Personal;

namespace Kira.UnitTests;

public class MatchingTests
{
    [Theory]
    [InlineData("PEMBAYARAN JOMPAY TNB 2209123", "tnb")]
    [InlineData("IBG TRF TO AHMAD ZAKI BIN ISMAIL", "ahmad zaki bin")]
    [InlineData("FPX META PLATFORMS IRELAND LTD", "meta platforms ireland")]
    [InlineData("12345 / 678", "")]
    public void Merchant_key(string description, string expected) => Assert.Equal(expected, MerchantKey.From(description));

    static readonly List<PaymentAccount> Accounts =
    [
        new() { Name = "Maybank Savings", Kind = PaymentKind.Bank, Institution = "Maybank", Last4 = "4412" },
        new() { Name = "CIMB Visa", Kind = PaymentKind.CreditCard, Institution = "CIMB", Last4 = "2201" },
        new() { Name = "CIMB Debit", Kind = PaymentKind.DebitCard, Institution = "CIMB", Last4 = "8814" },
        new() { Name = "Touch 'n Go", Kind = PaymentKind.EWallet, Institution = "Touch 'n Go", Last4 = "4410" },
        new() { Name = "Cash", Kind = PaymentKind.Cash }
    ];

    [Fact]
    public void Last_digits_win()
    {
        var m = PaymentMatcher.Match(Accounts, "Some bank", "xxxx 4412", null);
        Assert.Equal("Maybank Savings", m.Account?.Name);
        Assert.True(m.Confidence > 0.9m);
    }

    [Fact]
    public void Institution_and_kind_disambiguate()
    {
        var m = PaymentMatcher.Match(Accounts, "CIMB Bank", null, "credit_card");
        Assert.Equal("CIMB Visa", m.Account?.Name);
    }

    [Fact]
    public void Tng_aliases_match()
    {
        Assert.Equal("Touch 'n Go", PaymentMatcher.Match(Accounts, "TNG eWallet", null, "ewallet").Account?.Name);
    }

    [Fact]
    public void Unknown_bank_has_no_match()
    {
        var m = PaymentMatcher.Match(Accounts, "RHB", null, "bank");
        Assert.Null(m.Account);
        Assert.Contains("RHB", m.Why);
    }

    [Fact]
    public void Receipt_and_statement_line_are_the_same_payment_within_three_days()
    {
        var acc = Guid.NewGuid();
        var t = new PersonalTransaction { PaymentAccountId = acc, Amount = 104.62m, Direction = Direction.Out, Date = new DateOnly(2026, 9, 24) };
        Assert.True(t.IsSamePayment(acc, 104.62m, new DateOnly(2026, 9, 26)));
        Assert.False(t.IsSamePayment(acc, 104.62m, new DateOnly(2026, 9, 30)));
        Assert.False(t.IsSamePayment(acc, 104.60m, new DateOnly(2026, 9, 24)));
    }

    [Fact]
    public void Csv_with_malay_headers()
    {
        const string csv = "Tarikh,Keterangan,Pengeluaran,Simpanan,Baki\n03/09/2026,IBG CR KENANGA,,8800.00,20800.00\n05/09/2026,\"FPX AWS, EMEA\",742.18,,20057.82\n";
        var lines = StatementCsv.Parse(csv);
        Assert.Equal(2, lines.Count);
        Assert.Equal(Direction.In, lines[0].Direction);
        Assert.Equal(8800m, lines[0].Amount);
        Assert.Equal("FPX AWS, EMEA", lines[1].Description);
        Assert.Equal(Direction.Out, lines[1].Direction);
        Assert.Equal(new DateOnly(2026, 9, 5), lines[1].Date);
    }
}
