using commerce.Application.Commerce.Customer.Payments;
using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Customer.Packages;

// ── Get available packages ────────────────────────────────────────────────────

public sealed record GetAvailablePackagesQuery(Guid CustomerId, Guid BrandId) : IQuery<List<PackageDto>>;

public sealed class GetAvailablePackagesHandler : IQueryHandler<GetAvailablePackagesQuery, List<PackageDto>>
{
    private readonly ICommerceDbContext _db;

    public GetAvailablePackagesHandler(ICommerceDbContext db) => _db = db;

    public async Task<List<PackageDto>> HandleAsync(GetAvailablePackagesQuery q, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.Packages
            .Where(x => x.BrandId == q.BrandId
                     && x.DeletedAt == null
                     && x.Status == "active"
                     && (x.AvailableFrom == null || x.AvailableFrom <= now)
                     && (x.AvailableTo == null || x.AvailableTo >= now))
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new PackageDto(
                x.Id, x.BrandId, x.Code, x.Name, x.NameLocalized, x.Tier, x.Description,
                x.Price, x.CreditValue, x.DiscountPercent, x.CreditMultiplier,
                x.ValidityDays, x.IsUnlimitedValidity, x.ApplicableServices, x.ExcludedServices,
                x.MinimumOrderValue, x.MaxUsagePerOrder, x.MaxPurchasesPerCust,
                x.IconUrl, x.ColorHex, x.DisplayOrder, x.IsFeatured, x.TermsAndConditions,
                x.Status, x.AvailableFrom, x.AvailableTo, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);
    }
}

// ── Get my active packages ────────────────────────────────────────────────────

public sealed record GetMyPackagesQuery(Guid CustomerId, Guid BrandId) : IQuery<List<CustomerPackageDto>>;

public sealed class GetMyPackagesHandler : IQueryHandler<GetMyPackagesQuery, List<CustomerPackageDto>>
{
    private readonly ICommerceDbContext _db;

    public GetMyPackagesHandler(ICommerceDbContext db) => _db = db;

    public async Task<List<CustomerPackageDto>> HandleAsync(GetMyPackagesQuery q, CancellationToken ct)
    {
        return await _db.CustomerPackages
            .Include(x => x.Package)
            .Where(x => x.CustomerId == q.CustomerId && x.BrandId == q.BrandId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x))
            .ToListAsync(ct);
    }

    internal static CustomerPackageDto ToDto(CustomerPackage x) => new(
        x.Id, x.BrandId, x.CustomerId, x.PackageId,
        x.Package.Name,
        x.PurchaseAmount, x.CreditValueTotal, x.CreditValueUsed, x.CreditValueRemaining,
        x.ActivatedAt, x.ExpiresAt, x.IsUnlimitedValidity, x.LastUsedAt,
        x.UsageCount, x.Status, x.CreatedAt, x.UpdatedAt);
}

// ── Get my package usage ──────────────────────────────────────────────────────

public sealed record GetMyPackageUsageQuery(Guid CustomerId, Guid CustomerPackageId, Guid BrandId, int Page, int PageSize)
    : IQuery<PaginatedList<PackageUsageLedgerDto>>;

public sealed class GetMyPackageUsageHandler : IQueryHandler<GetMyPackageUsageQuery, PaginatedList<PackageUsageLedgerDto>>
{
    private readonly ICommerceDbContext _db;

    public GetMyPackageUsageHandler(ICommerceDbContext db) => _db = db;

    public Task<PaginatedList<PackageUsageLedgerDto>> HandleAsync(GetMyPackageUsageQuery q, CancellationToken ct)
    {
        var query = _db.PackageUsageLedger
            .Where(x => x.CustomerPackageId == q.CustomerPackageId
                     && x.CustomerId == q.CustomerId   // self-filter
                     && x.BrandId == q.BrandId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new PackageUsageLedgerDto(
                x.Id, x.CustomerPackageId, x.TransactionType,
                x.Amount, x.BalanceBefore, x.BalanceAfter,
                x.Notes, x.ReferenceType, x.ReferenceId,
                x.OccurredAt, x.CreatedAt));
        return PaginatedList<PackageUsageLedgerDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }
}

// ── Purchase Package: Initiate Payment ────────────────────────────────────────

public sealed record PurchasePackageInitiateCommand(
    Guid CustomerId,
    Guid BrandId,
    PurchasePackageRequest Request,
    string IdempotencyKey
) : ICommand<PaymentDto>;

public sealed class PurchasePackageInitiateHandler : ICommandHandler<PurchasePackageInitiateCommand, PaymentDto>
{
    private readonly ICommerceDbContext _db;
    private readonly IPaymentGateway _gateway;

    public PurchasePackageInitiateHandler(ICommerceDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<PaymentDto> HandleAsync(PurchasePackageInitiateCommand cmd, CancellationToken ct)
    {
        var pkg = await _db.Packages
            .FirstOrDefaultAsync(x => x.Id == cmd.Request.PackageId
                                   && x.BrandId == cmd.BrandId
                                   && x.DeletedAt == null
                                   && x.Status == "active", ct);

        if (pkg is null)
            throw new BusinessRuleException("Package not found or not available.");

        // Check max purchases per customer
        if (pkg.MaxPurchasesPerCust.HasValue)
        {
            var count = await _db.CustomerPackages
                .CountAsync(x => x.CustomerId == cmd.CustomerId && x.PackageId == pkg.Id, ct);
            if (count >= pkg.MaxPurchasesPerCust.Value)
                throw new BusinessRuleException($"Maximum purchase limit ({pkg.MaxPurchasesPerCust}) reached for this package.");
        }

        // Delegate to shared payment initiation (idempotent)
        var sender = new InitiatePaymentHandler(_db, _gateway);
        return await sender.HandleAsync(new InitiatePaymentCommand(
            cmd.CustomerId,
            cmd.BrandId,
            new InitiatePaymentRequest(pkg.Price, "INR", "package", cmd.Request.PaymentMethodId, null, null, cmd.Request.Notes),
            cmd.IdempotencyKey), ct);
    }
}

public sealed class PurchasePackageInitiateValidator : AbstractValidator<PurchasePackageRequest>
{
    public PurchasePackageInitiateValidator()
    {
        RuleFor(x => x.PackageId).NotEmpty();
    }
}

// ── Package Payment Verify → activate CustomerPackage + ledger credit ─────────

public sealed record PurchasePackageVerifyCommand(
    Guid CustomerId,
    Guid BrandId,
    VerifyPaymentRequest Request
) : ICommand<CustomerPackageDto>;

public sealed class PurchasePackageVerifyHandler : ICommandHandler<PurchasePackageVerifyCommand, CustomerPackageDto>
{
    private readonly ICommerceDbContext _db;
    private readonly IPaymentGateway _gateway;

    public PurchasePackageVerifyHandler(ICommerceDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<CustomerPackageDto> HandleAsync(PurchasePackageVerifyCommand cmd, CancellationToken ct)
    {
        // First verify the payment via shared handler
        var verifyHandler = new VerifyPaymentHandler(_db, _gateway);
        var paymentDto = await verifyHandler.HandleAsync(
            new VerifyPaymentCommand(cmd.CustomerId, cmd.BrandId, cmd.Request), ct);

        // Retrieve the now-captured payment
        var payment = await _db.Payments.FirstAsync(p => p.Id == paymentDto.Id, ct);

        // Idempotency: check if customer_package was already activated for this payment
        var existingCp = await _db.CustomerPackages
            .Include(x => x.Package)
            .FirstOrDefaultAsync(x => x.PaymentId == payment.Id, ct);
        if (existingCp is not null)
            return GetMyPackagesHandler.ToDto(existingCp);

        // Find the package by matching amount (or we could store it on the payment notes/metadata)
        // We look for an active package matching the payment amount for this brand
        var pkg = await _db.Packages
            .FirstOrDefaultAsync(x => x.BrandId == cmd.BrandId
                                   && x.Price == payment.Amount
                                   && x.DeletedAt == null, ct);
        if (pkg is null)
            throw new BusinessRuleException("Cannot identify package for this payment. Contact support.");

        var now = DateTimeOffset.UtcNow;

        CustomerPackage activatedCp = null!;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            var cp = new CustomerPackage
            {
                Id                  = Guid.NewGuid(),
                BrandId             = cmd.BrandId,
                CustomerId          = cmd.CustomerId,
                PackageId           = pkg.Id,
                PaymentId           = payment.Id,
                PurchaseAmount      = payment.Amount,
                CreditValueTotal    = pkg.CreditValue,
                CreditValueUsed     = 0m,
                ActivatedAt         = now,
                ExpiresAt           = pkg.IsUnlimitedValidity ? null : now.AddDays(pkg.ValidityDays ?? 365),
                IsUnlimitedValidity = pkg.IsUnlimitedValidity,
                UsageCount          = 0,
                Status              = "active",
                Metadata            = "{}",
                CreatedAt           = now,
                UpdatedAt           = now,
                CreatedBy           = cmd.CustomerId
            };

            _db.CustomerPackages.Add(cp);
            await _db.SaveChangesAsync(innerCt);

            // Append initial ledger credit (INSERT-only)
            _db.PackageUsageLedger.Add(new PackageUsageLedger
            {
                Id                = Guid.NewGuid(),
                CustomerPackageId = cp.Id,
                BrandId           = cmd.BrandId,
                CustomerId        = cmd.CustomerId,
                TransactionType   = "credit",
                Amount            = pkg.CreditValue,
                BalanceBefore     = 0m,
                BalanceAfter      = pkg.CreditValue,
                Notes             = $"Package purchased: {pkg.Name}",
                ReferenceType     = "payment",
                ReferenceId       = payment.Id,
                PerformedBy       = cmd.CustomerId,
                OccurredAt        = now,
                CreatedAt         = now,
                CreatedBy         = cmd.CustomerId
            });

            await _db.SaveChangesAsync(innerCt);
            activatedCp = cp;
        }, ct);

        // Reload with navigation for DTO projection
        var result = await _db.CustomerPackages
            .Include(x => x.Package)
            .FirstAsync(x => x.Id == activatedCp.Id, ct);

        return GetMyPackagesHandler.ToDto(result);
    }
}
