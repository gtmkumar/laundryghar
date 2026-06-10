using FluentValidation;
using laundryghar.Finance.Application.Expenses.Dtos;
using MediatR;

namespace laundryghar.Finance.Application.Expenses.Commands;

// ── Shared mapping ────────────────────────────────────────────────────────────

internal static class ExpenseMapper
{
    internal static ExpenseCategoryDto ToCategoryDto(ExpenseCategory c) => new(
        c.Id, c.BrandId, c.ParentId, c.Code, c.Name,
        c.Description, c.IsTaxDeductible, c.RequiresApproval,
        c.ApprovalThreshold, c.AccountingCode, c.DisplayOrder,
        c.IsActive, c.Status, c.CreatedAt);

    internal static ExpenseDto ToDto(Expense e) => new(
        e.Id, e.BrandId, e.FranchiseId, e.StoreId,
        e.CategoryId, e.Category?.Name ?? string.Empty,
        e.ExpenseNumber, e.ExpenseDate,
        e.Amount, e.TaxAmount, e.TotalAmount,
        e.PaymentMode, e.VendorName, e.BillNumber,
        e.Description, e.Notes, e.IsRecurring, e.RecurrenceFrequency,
        e.IsReimbursable, e.Status, e.SubmittedAt,
        e.ApprovedAt, e.PaidAt, e.RejectionReason,
        e.CreatedAt,
        e.Attachments
            .Where(a => a.DeletedAt == null)
            .Select(ToAttachmentDto)
            .ToList());

    internal static ExpenseAttachmentDto ToAttachmentDto(ExpenseAttachment a) => new(
        a.Id, a.ExpenseId, a.S3Key, a.FileName, a.MimeType,
        a.Bytes, a.DocumentType, a.IsPrimary, a.CdnUrl, a.UploadedAt);
}

// ── Expense Category Commands ─────────────────────────────────────────────────

public sealed record CreateExpenseCategoryCommand(CreateExpenseCategoryRequest Request, Guid? ActorId)
    : IRequest<ExpenseCategoryDto>;

public sealed class CreateExpenseCategoryHandler
    : IRequestHandler<CreateExpenseCategoryCommand, ExpenseCategoryDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public CreateExpenseCategoryHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseCategoryDto> Handle(CreateExpenseCategoryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        var exists = await _db.ExpenseCategories
            .AnyAsync(c => c.BrandId == brandId && c.Code == req.Code, ct);
        if (exists)
            throw new BusinessRuleException($"Expense category with code '{req.Code}' already exists.");

        var cat = new ExpenseCategory
        {
            Id                = Guid.NewGuid(),
            BrandId           = brandId,
            ParentId          = req.ParentId,
            Code              = req.Code,
            Name              = req.Name,
            NameLocalized     = "{}",
            Description       = req.Description,
            IsTaxDeductible   = req.IsTaxDeductible,
            RequiresApproval  = req.RequiresApproval,
            ApprovalThreshold = req.ApprovalThreshold,
            AccountingCode    = req.AccountingCode,
            DisplayOrder      = req.DisplayOrder,
            IsActive          = true,
            Status            = "active",
            CreatedAt         = now,
            UpdatedAt         = now,
            CreatedBy         = cmd.ActorId,
            UpdatedBy         = cmd.ActorId
        };

        _db.ExpenseCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToCategoryDto(cat);
    }
}

public sealed record UpdateExpenseCategoryCommand(Guid Id, UpdateExpenseCategoryRequest Request, Guid? ActorId)
    : IRequest<ExpenseCategoryDto?>;

public sealed class UpdateExpenseCategoryHandler
    : IRequestHandler<UpdateExpenseCategoryCommand, ExpenseCategoryDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public UpdateExpenseCategoryHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseCategoryDto?> Handle(UpdateExpenseCategoryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var cat = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == cmd.Id && c.BrandId == brandId, ct);
        if (cat is null) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;
        cat.Name              = req.Name;
        cat.Description       = req.Description;
        cat.IsTaxDeductible   = req.IsTaxDeductible;
        cat.RequiresApproval  = req.RequiresApproval;
        cat.ApprovalThreshold = req.ApprovalThreshold;
        cat.AccountingCode    = req.AccountingCode;
        cat.DisplayOrder      = req.DisplayOrder;
        cat.Status            = req.Status;
        cat.IsActive          = req.Status == "active";
        cat.UpdatedAt         = now;
        cat.UpdatedBy         = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToCategoryDto(cat);
    }
}

public sealed class CreateExpenseCategoryValidator : AbstractValidator<CreateExpenseCategoryCommand>
{
    public CreateExpenseCategoryValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateExpenseCategoryValidator : AbstractValidator<UpdateExpenseCategoryCommand>
{
    private static readonly string[] ValidStatuses = ["active", "inactive", "archived"];
    public UpdateExpenseCategoryValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Status)
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}.");
    }
}

// ── Create Expense ────────────────────────────────────────────────────────────

public sealed record CreateExpenseCommand(CreateExpenseRequest Request, Guid? ActorId)
    : IRequest<ExpenseDto>;

public sealed class CreateExpenseHandler : IRequestHandler<CreateExpenseCommand, ExpenseDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public CreateExpenseHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseDto> Handle(CreateExpenseCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Verify franchise belongs to brand (cross-brand IDOR guard).
        var franchiseInBrand = await _db.Franchises
            .AnyAsync(f => f.Id == req.FranchiseId && f.BrandId == brandId, ct);
        if (!franchiseInBrand)
            throw new KeyNotFoundException("Franchise not found.");

        // Verify store belongs to brand, when supplied.
        if (req.StoreId.HasValue)
        {
            var storeInBrand = await _db.Stores
                .AnyAsync(s => s.Id == req.StoreId.Value && s.BrandId == brandId, ct);
            if (!storeInBrand)
                throw new KeyNotFoundException("Store not found.");
        }

        // Verify warehouse belongs to brand, when supplied.
        if (req.WarehouseId.HasValue)
        {
            var warehouseInBrand = await _db.Warehouses
                .AnyAsync(w => w.Id == req.WarehouseId.Value && w.BrandId == brandId, ct);
            if (!warehouseInBrand)
                throw new KeyNotFoundException("Warehouse not found.");
        }

        // Verify category belongs to brand
        var catExists = await _db.ExpenseCategories
            .AnyAsync(c => c.Id == req.CategoryId && c.BrandId == brandId && c.Status == "active", ct);
        if (!catExists)
            throw new BusinessRuleException("Expense category not found or inactive.");

        // Generate expense number
        var count  = await _db.Expenses.CountAsync(e => e.BrandId == brandId, ct);
        var expNum = $"EXP-{now:yyyyMMdd}-{(count + 1):D5}";

        var status = req.SubmitNow ? "submitted" : "draft";

        var expense = new Expense
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            FranchiseId         = req.FranchiseId,
            StoreId             = req.StoreId,
            WarehouseId         = req.WarehouseId,
            CategoryId          = req.CategoryId,
            ExpenseNumber       = expNum,
            ExpenseDate         = req.ExpenseDate,
            Amount              = req.Amount,
            TaxAmount           = req.TaxAmount,
            // TotalAmount is generated — do NOT set
            PaymentMode         = req.PaymentMode,
            Description         = req.Description,
            VendorName          = req.VendorName,
            VendorGstin         = req.VendorGstin,
            VendorPhone         = req.VendorPhone,
            BillNumber          = req.BillNumber,
            BillDate            = req.BillDate,
            Notes               = req.Notes,
            IsRecurring         = req.IsRecurring,
            RecurrenceFrequency = req.RecurrenceFrequency,
            IsReimbursable      = req.IsReimbursable,
            RequiresApproval    = req.RequiresApproval,
            Status              = status,
            SubmittedBy         = cmd.ActorId ?? Guid.Empty,
            SubmittedAt         = now,
            Metadata            = "{}",
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId
        };

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        // Reload to get generated TotalAmount and Category nav
        await _db.Entry(expense).ReloadAsync(ct);
        await _db.Entry(expense).Reference(e => e.Category).LoadAsync(ct);

        return ExpenseMapper.ToDto(expense);
    }
}

public sealed class CreateExpenseValidator : AbstractValidator<CreateExpenseCommand>
{
    private static readonly string[] ValidPaymentModes =
        ["cash", "upi", "card", "bank_transfer", "cheque", "credit"];
    private static readonly string[] ValidFrequencies =
        ["weekly", "monthly", "quarterly", "yearly"];

    public CreateExpenseValidator()
    {
        RuleFor(x => x.Request.FranchiseId).NotEmpty();
        RuleFor(x => x.Request.CategoryId).NotEmpty();
        RuleFor(x => x.Request.ExpenseDate).NotEqual(default(DateOnly));
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.TaxAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.Description).NotEmpty();
        RuleFor(x => x.Request.PaymentMode)
            .Must(p => ValidPaymentModes.Contains(p))
            .WithMessage($"PaymentMode must be one of: {string.Join(", ", ValidPaymentModes)}.");
        RuleFor(x => x.Request.RecurrenceFrequency)
            .Must(f => f == null || ValidFrequencies.Contains(f))
            .WithMessage($"RecurrenceFrequency must be one of: {string.Join(", ", ValidFrequencies)}.");
    }
}

// ── Approve Expense ───────────────────────────────────────────────────────────

public sealed record ApproveExpenseCommand(Guid Id, ApproveExpenseRequest Request, Guid? ActorId)
    : IRequest<ExpenseDto?>;

public sealed class ApproveExpenseHandler : IRequestHandler<ApproveExpenseCommand, ExpenseDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public ApproveExpenseHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseDto?> Handle(ApproveExpenseCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var expense = await _db.Expenses.Include(e => e.Attachments)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == cmd.Id && e.BrandId == brandId, ct);
        if (expense is null) return null;

        if (expense.Status != "submitted")
            throw new BusinessRuleException($"Only submitted expenses can be approved. Current status: '{expense.Status}'.");

        var now = DateTimeOffset.UtcNow;
        expense.Status     = "approved";
        expense.ApprovedBy = cmd.ActorId;
        expense.ApprovedAt = now;
        expense.Notes      = cmd.Request.Notes ?? expense.Notes;
        expense.UpdatedAt  = now;
        expense.UpdatedBy  = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToDto(expense);
    }
}

// ── Reject Expense ────────────────────────────────────────────────────────────

public sealed record RejectExpenseCommand(Guid Id, RejectExpenseRequest Request, Guid? ActorId)
    : IRequest<ExpenseDto?>;

public sealed class RejectExpenseHandler : IRequestHandler<RejectExpenseCommand, ExpenseDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public RejectExpenseHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseDto?> Handle(RejectExpenseCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var expense = await _db.Expenses.Include(e => e.Attachments)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == cmd.Id && e.BrandId == brandId, ct);
        if (expense is null) return null;

        if (expense.Status != "submitted")
            throw new BusinessRuleException($"Only submitted expenses can be rejected. Current status: '{expense.Status}'.");

        var now = DateTimeOffset.UtcNow;
        expense.Status          = "rejected";
        expense.RejectedBy      = cmd.ActorId;
        expense.RejectedAt      = now;
        expense.RejectionReason = cmd.Request.RejectionReason;
        expense.UpdatedAt       = now;
        expense.UpdatedBy       = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToDto(expense);
    }
}

public sealed class RejectExpenseValidator : AbstractValidator<RejectExpenseCommand>
{
    public RejectExpenseValidator()
    {
        RuleFor(x => x.Request.RejectionReason).NotEmpty();
    }
}

// ── Mark Expense Paid ─────────────────────────────────────────────────────────

public sealed record MarkExpensePaidCommand(Guid Id, MarkExpensePaidRequest Request, Guid? ActorId)
    : IRequest<ExpenseDto?>;

public sealed class MarkExpensePaidHandler : IRequestHandler<MarkExpensePaidCommand, ExpenseDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public MarkExpensePaidHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseDto?> Handle(MarkExpensePaidCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var expense = await _db.Expenses.Include(e => e.Attachments)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == cmd.Id && e.BrandId == brandId, ct);
        if (expense is null) return null;

        if (expense.Status != "approved")
            throw new BusinessRuleException($"Only approved expenses can be marked as paid. Current status: '{expense.Status}'.");

        var now = DateTimeOffset.UtcNow;
        expense.Status    = "paid";
        expense.PaidAt    = now;
        expense.Notes     = cmd.Request.Notes ?? expense.Notes;
        expense.UpdatedAt = now;
        expense.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToDto(expense);
    }
}

// ── Add Expense Attachment ────────────────────────────────────────────────────

public sealed record AddExpenseAttachmentCommand(Guid ExpenseId, AddExpenseAttachmentRequest Request, Guid? ActorId)
    : IRequest<ExpenseAttachmentDto?>;

public sealed class AddExpenseAttachmentHandler
    : IRequestHandler<AddExpenseAttachmentCommand, ExpenseAttachmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser         _user;
    public AddExpenseAttachmentHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<ExpenseAttachmentDto?> Handle(AddExpenseAttachmentCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var expense = await _db.Expenses
            .FirstOrDefaultAsync(e => e.Id == cmd.ExpenseId && e.BrandId == brandId, ct);
        if (expense is null) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var attachment = new ExpenseAttachment
        {
            Id           = Guid.NewGuid(),
            ExpenseId    = expense.Id,
            BrandId      = brandId,
            S3Key        = req.S3Key,
            FileName     = req.FileName,
            MimeType     = req.MimeType,
            Bytes        = req.Bytes,
            DocumentType = req.DocumentType,
            IsPrimary    = req.IsPrimary,
            CdnUrl       = req.CdnUrl,
            UploadedBy   = cmd.ActorId,
            UploadedAt   = now,
            CreatedAt    = now,
            CreatedBy    = cmd.ActorId
        };

        _db.ExpenseAttachments.Add(attachment);
        await _db.SaveChangesAsync(ct);
        return ExpenseMapper.ToAttachmentDto(attachment);
    }
}

public sealed class AddExpenseAttachmentValidator : AbstractValidator<AddExpenseAttachmentCommand>
{
    private static readonly string[] ValidDocTypes =
        ["receipt", "invoice", "bill", "quotation", "other"];

    public AddExpenseAttachmentValidator()
    {
        RuleFor(x => x.Request.S3Key).NotEmpty();
        RuleFor(x => x.Request.FileName).NotEmpty();
        RuleFor(x => x.Request.MimeType).NotEmpty();
        RuleFor(x => x.Request.DocumentType)
            .Must(d => d == null || ValidDocTypes.Contains(d))
            .WithMessage($"DocumentType must be one of: {string.Join(", ", ValidDocTypes)}.");
    }
}
