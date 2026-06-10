using FluentValidation;
using laundryghar.Finance.Application.CashBooks.Commands;
using laundryghar.Finance.Application.CashBooks.Dtos;
using laundryghar.Finance.Application.Expenses.Commands;
using laundryghar.Finance.Application.Expenses.Dtos;

namespace laundryghar.Finance.Tests.Validators;

/// <summary>
/// Validator unit tests for Finance command validators.
///
/// Key invariants:
///   - CloseCashBookValidator: closing balance must be >= 0 (cash is physical; no negative cash)
///   - CreateShiftHandoverValidator: cash_handed_over must be >= 0; counts >= 0
///   - AddCashBookEntryValidator: amount > 0 (no zero/negative ledger entries);
///     direction exactly 1 or -1; enums mirror DB CHECK constraints
///   - OpenCashBookValidator: opening balance >= 0; shift label in allowed set
///   - CreateExpenseValidator: amount > 0; tax >= 0; payment_mode in allowed set
/// </summary>
public sealed class FinanceValidatorTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // CloseCashBookValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly CloseCashBookValidator _closeValidator = new();

    private static CloseCashBookCommand CloseCmd(decimal closingBalance, string? reason = null) =>
        new(Guid.NewGuid(), new CloseCashBookRequest(closingBalance, reason, null), null);

    [Theory]
    [InlineData(0)]
    [InlineData(100.50)]
    [InlineData(99999.99)]
    public void CloseCashBook_NonNegativeBalance_Passes(decimal balance)
        => Assert.True(_closeValidator.Validate(CloseCmd(balance)).IsValid);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void CloseCashBook_NegativeBalance_Fails(decimal balance)
    {
        var result = _closeValidator.Validate(CloseCmd(balance));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("ClosingBalance"));
    }

    [Fact]
    public void CloseCashBook_VarianceReasonExceeds500_Fails()
    {
        var result = _closeValidator.Validate(CloseCmd(100m, new string('x', 501)));
        Assert.False(result.IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CreateShiftHandoverValidator
    // ────────────────────────────────────────────────────────────────────────────

    private readonly CreateShiftHandoverValidator _handoverValidator = new();

    private static CreateShiftHandoverCommand HandoverCmd(
        decimal cash, int pendingOrders = 0, int pickups = 0, int deliveries = 0) =>
        new(new CreateShiftHandoverRequest(
            Guid.NewGuid(),   // StoreId
            Guid.NewGuid(),   // FromUserId
            null,             // ToUserId (optional)
            cash,
            pendingOrders,
            0,                // OpenComplaintsCount
            pickups,
            deliveries,
            null,             // NotesFrom
            null              // CashBookId
        ), null);

    [Theory]
    [InlineData(0)]
    [InlineData(500.00)]
    [InlineData(10000)]
    public void ShiftHandover_NonNegativeCash_Passes(decimal cash)
        => Assert.True(_handoverValidator.Validate(HandoverCmd(cash)).IsValid);

    [Fact]
    public void ShiftHandover_NegativeCash_Fails()
    {
        var result = _handoverValidator.Validate(HandoverCmd(-1m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("CashHandedOver"));
    }

    [Fact]
    public void ShiftHandover_NegativePendingOrders_Fails()
    {
        var result = _handoverValidator.Validate(HandoverCmd(0m, pendingOrders: -1));
        Assert.False(result.IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // AddCashBookEntryValidator — amount > 0 is the key money-cap rule
    // ────────────────────────────────────────────────────────────────────────────

    private readonly AddCashBookEntryValidator _entryValidator = new();

    private static AddCashBookEntryCommand EntryCmd(
        decimal amount, int direction = 1,
        string entryType = "cash_in", string category = "order_payment",
        string paymentMode = "cash") =>
        new(Guid.NewGuid(),
            new AddCashBookEntryRequest(entryType, category, (short)direction, amount, paymentMode,
                null, null, null, null),
            null);

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(999999.99)]
    public void CashBookEntry_PositiveAmount_Passes(decimal amount)
        => Assert.True(_entryValidator.Validate(EntryCmd(amount)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void CashBookEntry_NonPositiveAmount_Fails(decimal amount)
    {
        var result = _entryValidator.Validate(EntryCmd(amount));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Amount"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CashBookEntry_ValidDirection_Passes(int direction)
        => Assert.True(_entryValidator.Validate(EntryCmd(10m, direction)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    public void CashBookEntry_InvalidDirection_Fails(int direction)
    {
        var result = _entryValidator.Validate(EntryCmd(10m, direction));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("cash_in",   "order_payment", "cash")]
    [InlineData("cash_out",  "expense",       "cash")]
    [InlineData("deposit",   "deposit",       "bank_transfer")]
    [InlineData("adjustment","other",         "other")]
    public void CashBookEntry_ValidEnumCombinations_Passes(
        string entryType, string category, string paymentMode)
        => Assert.True(_entryValidator.Validate(EntryCmd(50m, 1, entryType, category, paymentMode)).IsValid);

    [Fact]
    public void CashBookEntry_UnknownEntryType_Fails()
    {
        var result = _entryValidator.Validate(EntryCmd(10m, 1, "invoice"));   // not a valid entry_type
        Assert.False(result.IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // OpenCashBookValidator — shift labels must match DB CHECK
    // ────────────────────────────────────────────────────────────────────────────

    private readonly OpenCashBookValidator _openValidator = new();

    [Theory]
    [InlineData("morning")]
    [InlineData("afternoon")]
    [InlineData("evening")]
    [InlineData("night")]
    [InlineData("full_day")]
    public void OpenCashBook_ValidShift_Passes(string shift)
    {
        var cmd = new OpenCashBookCommand(
            new OpenCashBookRequest(Guid.NewGuid(), Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow), shift, 0m), null);
        Assert.True(_openValidator.Validate(cmd).IsValid);
    }

    [Theory]
    [InlineData("day")]
    [InlineData("")]
    [InlineData("MORNING")]
    public void OpenCashBook_InvalidShift_Fails(string shift)
    {
        var cmd = new OpenCashBookCommand(
            new OpenCashBookRequest(Guid.NewGuid(), Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow), shift, 0m), null);
        Assert.False(_openValidator.Validate(cmd).IsValid);
    }

    [Fact]
    public void OpenCashBook_NegativeOpeningBalance_Fails()
    {
        var cmd = new OpenCashBookCommand(
            new OpenCashBookRequest(Guid.NewGuid(), Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow), "full_day", -1m), null);
        Assert.False(_openValidator.Validate(cmd).IsValid);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CreateExpenseValidator — amount > 0; tax >= 0
    // ────────────────────────────────────────────────────────────────────────────

    private readonly CreateExpenseValidator _expenseValidator = new();

    private static CreateExpenseCommand ExpenseCmd(decimal amount, decimal tax = 0m) =>
        new(new CreateExpenseRequest(
            Guid.NewGuid(),         // FranchiseId
            null,                   // StoreId
            null,                   // WarehouseId
            Guid.NewGuid(),         // CategoryId
            DateOnly.FromDateTime(DateTime.UtcNow),
            amount,
            tax,
            "cash",
            "Monthly utility bill",
            null, null, null, null, null,
            null, false, null, false, false, true),
        null);

    [Fact]
    public void CreateExpense_ValidAmount_Passes()
        => Assert.True(_expenseValidator.Validate(ExpenseCmd(250m)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateExpense_NonPositiveAmount_Fails(decimal amount)
    {
        var result = _expenseValidator.Validate(ExpenseCmd(amount));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Amount"));
    }

    [Fact]
    public void CreateExpense_NegativeTax_Fails()
    {
        var result = _expenseValidator.Validate(ExpenseCmd(100m, -0.01m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("TaxAmount"));
    }

    [Fact]
    public void CreateExpense_ZeroTax_Passes()
        => Assert.True(_expenseValidator.Validate(ExpenseCmd(100m, 0m)).IsValid);
}
