namespace laundryghar.SharedDataModel.Enums;

/// <summary>
/// Marketplace job kind on an order. Orthogonal to <see cref="OrderType"/> (which is
/// the laundry sub-classification). <c>Laundry</c> is the default and reproduces the
/// existing laundry workflow; <c>Parcel</c> is a point-to-point delivery that reuses
/// the same order / state-machine / payment / dispatch spine. Extensible later to
/// truck / intercity.
/// </summary>
public static class JobType
{
    public const string Laundry = "laundry";
    public const string Parcel = "parcel";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Laundry, Parcel };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
