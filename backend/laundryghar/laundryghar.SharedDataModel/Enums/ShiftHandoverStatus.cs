namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Matches finance_royalty.shift_handovers.status CHECK constraint exactly.
/// Values: pending, acknowledged, disputed, closed.
/// </summary>
public enum ShiftHandoverStatus
{
    Pending,
    Acknowledged,
    Disputed,
    Closed
}
