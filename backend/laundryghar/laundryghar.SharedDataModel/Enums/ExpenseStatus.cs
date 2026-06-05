namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.expenses.status CHECK constraint exactly.
/// Values: draft, submitted, approved, rejected, paid, reconciled, disputed.
/// </summary>
public enum ExpenseStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Paid,
    Reconciled,
    Disputed
}
