using Kira.Domain.Common;
using Kira.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kira.Infrastructure.Persistence;

/// <summary>
/// The ledger is append-only. Posted lines never change, entries are never deleted, and the only field of a posted
/// entry that may change is the link to the entry that reverses it. Corrections are made by posting a reversal.
/// </summary>
public sealed class LedgerImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    static void Check(DbContext? context)
    {
        if (context == null) return;
        foreach (var e in context.ChangeTracker.Entries<JournalLine>())
            if (e.State is EntityState.Modified or EntityState.Deleted)
                throw new DomainException("Posted journal lines can't be changed. Post a reversal instead.");

        foreach (var e in context.ChangeTracker.Entries<JournalEntry>())
        {
            if (e.State == EntityState.Deleted)
                throw new DomainException("Posted journal entries can't be deleted. Post a reversal instead.");
            if (e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(JournalEntry.ReversedByEntryId)))
                throw new DomainException("Posted journal entries can't be edited. Post a reversal instead.");
        }
    }
}
