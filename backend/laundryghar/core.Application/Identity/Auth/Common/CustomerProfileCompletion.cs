using core.Application.Identity.Auth.Dtos;
using CustomerEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer;

namespace core.Application.Identity.Auth.Common;

/// <summary>
/// Scores how much of a customer's profile is filled in, for the dashboard's
/// "complete your profile" ribbon.
///
/// This is advisory only. Sign-up asks for nothing beyond the credential the customer
/// signed in with, and nothing here gates access to the app — order placement collects
/// the address and any other mandatory details at the point it needs them.
/// </summary>
public static class CustomerProfileCompletion
{
    public const string FieldName  = "name";
    public const string FieldEmail = "email";
    public const string FieldPhone = "phone";

    /// <summary>The fields the ribbon tracks, in the order the client should prompt for them.</summary>
    private static readonly string[] TrackedFields = [FieldName, FieldEmail, FieldPhone];

    public static ProfileCompletionDto Evaluate(CustomerEntity customer)
    {
        var missing = new List<string>(TrackedFields.Length);

        if (string.IsNullOrWhiteSpace(customer.DisplayName)
            && string.IsNullOrWhiteSpace(customer.FirstName))
            missing.Add(FieldName);

        if (string.IsNullOrWhiteSpace(customer.Email))
            missing.Add(FieldEmail);

        if (string.IsNullOrWhiteSpace(customer.PhoneE164))
            missing.Add(FieldPhone);

        var filled = TrackedFields.Length - missing.Count;
        var percent = (int)Math.Round(filled * 100.0 / TrackedFields.Length);

        return new ProfileCompletionDto(
            IsComplete: missing.Count == 0,
            PercentComplete: percent,
            MissingFields: missing);
    }

    /// <summary>Convenience for the token responses, which only carry the boolean.</summary>
    public static bool IsComplete(CustomerEntity customer) => Evaluate(customer).IsComplete;
}
