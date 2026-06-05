namespace laundryghar.Utilities.Common;

public class PaginatedList<T>
{
    public IReadOnlyList<T> List { get; }
    private int PageNumber { get; }
    private int PageCount { get; }
    private int TotalCount { get; }

    private PaginatedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (totalCount < 0) throw new ArgumentOutOfRangeException(nameof(totalCount));

        List = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageCount = (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < PageCount;

    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, count, pageNumber, pageSize);
    }
}
