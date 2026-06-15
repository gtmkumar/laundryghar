using commerce.Application.Analytics.Reporting.Dtos;
using commerce.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.Extensions.Logging;

namespace commerce.Application.Analytics.Reporting.Commands;

public sealed record RefreshMatviewsCommand : ICommand<RefreshResultDto>;

public sealed class RefreshMatviewsHandler : ICommandHandler<RefreshMatviewsCommand, RefreshResultDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ILogger<RefreshMatviewsHandler> _logger;

    public RefreshMatviewsHandler(
        ICommerceDbContext db,
        ICurrentUser user,
        ILogger<RefreshMatviewsHandler> logger)
    {
        _db     = db;
        _user   = user;
        _logger = logger;
    }

    public async Task<RefreshResultDto> HandleAsync(RefreshMatviewsCommand c, CancellationToken ct)
    {
        // Ensure caller has brand context (validates auth context is complete).
        _ = _user.RequireBrandId();

        // Refresh via the SECURITY DEFINER function: the matviews are owned by postgres and this
        // service connects as app_user, so a direct REFRESH is permission-denied. The function
        // (owned by postgres, RLS-exempt) refreshes all matviews CONCURRENTLY across every brand.
        // The same function backs the periodic matview refresh background service.
        // See db/patches/analytics_refresh_function.sql.
        try
        {
            await _db.ExecuteSqlRawAsync("SELECT analytics.refresh_all_matviews();", ct);
            _logger.LogInformation("Analytics matviews refreshed via refresh_all_matviews().");
            return new RefreshResultDto(Refreshed: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analytics matview refresh failed.");
            return new RefreshResultDto(Refreshed: false, Error: ex.Message);
        }
    }
}
