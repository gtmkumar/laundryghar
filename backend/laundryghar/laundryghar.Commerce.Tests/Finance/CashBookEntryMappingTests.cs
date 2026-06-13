using laundryghar.Finance.Application.CashBooks.Commands;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;

namespace laundryghar.Commerce.Tests.Finance;

/// <summary>
/// Regression for DEFECT B: POST /api/v1/admin/cash-books/{id}/entries returned the newly
/// added entry twice in the response aggregate's entries[]. Root cause: after SaveChanges
/// the handler manually called <c>book.Entries.Add(entry)</c> even though EF relationship
/// fix-up had already placed the tracked new entry into <c>book.Entries</c> (the book was
/// loaded with <c>.Include(b =&gt; b.Entries)</c>) — so the mapper saw it twice.
///
/// <see cref="CashBookMapper.ToDto"/> maps the navigation one-to-one, so these tests pin
/// that contract: the response DTO must contain exactly one row per entry in the tracked
/// collection, and a doubled collection (the old bug) would surface as duplicates.
/// </summary>
public sealed class CashBookEntryMappingTests
{
    private static CashBook NewBook(params CashBookEntry[] entries)
    {
        var book = new CashBook
        {
            Id          = Guid.NewGuid(),
            BrandId     = Guid.NewGuid(),
            FranchiseId = Guid.NewGuid(),
            StoreId     = Guid.NewGuid(),
            BookDate    = new DateOnly(2026, 6, 13),
            ShiftLabel  = "day",
            Status      = "open",
            Metadata    = "{}",
        };
        foreach (var e in entries) book.Entries.Add(e);
        return book;
    }

    private static CashBookEntry NewEntry(Guid book) => new()
    {
        Id          = Guid.NewGuid(),
        CashBookId  = book,
        EntryType   = "sale",
        Category    = "order_payment",
        Direction   = 1,
        Amount      = 100m,
        PaymentMode = "cash",
        Metadata    = "{}",
    };

    [Fact]
    public void ToDto_MapsEachEntryExactlyOnce()
    {
        var book = NewBook();
        var e1 = NewEntry(book.Id);
        var e2 = NewEntry(book.Id);
        var e3 = NewEntry(book.Id);
        book.Entries.Add(e1);
        book.Entries.Add(e2);
        book.Entries.Add(e3);

        var dto = CashBookMapper.ToDto(book);

        // 3 distinct rows in the collection → 3 (not 4) rows in the response.
        Assert.Equal(3, dto.Entries.Count);
        Assert.Equal(
            new[] { e1.Id, e2.Id, e3.Id }.OrderBy(x => x),
            dto.Entries.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void ToDto_NoDuplicateIds_WhenCollectionIsClean()
    {
        var book = NewBook();
        var newlyAdded = NewEntry(book.Id);
        book.Entries.Add(NewEntry(book.Id));
        book.Entries.Add(NewEntry(book.Id));
        book.Entries.Add(newlyAdded); // the just-added entry, present once (post-fix state)

        var dto = CashBookMapper.ToDto(book);

        Assert.Equal(dto.Entries.Count, dto.Entries.Select(x => x.Id).Distinct().Count());
        Assert.Single(dto.Entries.Where(x => x.Id == newlyAdded.Id));
    }

    [Fact]
    public void ToDto_DoubledEntry_ProducesDuplicate_DemonstratingTheOldBug()
    {
        // If the handler re-adds the same tracked reference (the pre-fix bug), the mapper
        // faithfully reflects the doubled collection. This documents the failure mode the
        // fix removes — it must NOT be how the handler builds the response.
        var book = NewBook();
        var entry = NewEntry(book.Id);
        book.Entries.Add(entry);
        book.Entries.Add(entry); // duplicate reference

        var dto = CashBookMapper.ToDto(book);

        Assert.Equal(2, dto.Entries.Count);
        Assert.Equal(2, dto.Entries.Count(x => x.Id == entry.Id));
    }
}
