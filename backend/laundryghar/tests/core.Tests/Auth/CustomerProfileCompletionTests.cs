using core.Application.Identity.Auth.Common;
using CustomerEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer;
using Xunit;

namespace core.Tests.Auth;

/// <summary>
/// The completion score drives the dashboard ribbon. It must never report "complete" for
/// an account that is missing something, and equally must not nag an account that is done.
/// </summary>
public class CustomerProfileCompletionTests
{
    private static CustomerEntity Customer(
        string? displayName = null,
        string? firstName = null,
        string? email = null,
        string? phone = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            CustomerCode = "TESTCODE01",
            DisplayName = displayName,
            FirstName = firstName,
            Email = email,
            PhoneE164 = phone,
            Locale = "en-IN",
            Timezone = "Asia/Kolkata",
            Status = "active",
            Metadata = "{}",
        };

    [Fact]
    public void Fresh_google_signup_is_missing_only_the_phone()
    {
        // Google supplies name + email but never a phone number, which is exactly the
        // state the "add your mobile number" nudge exists for.
        var completion = CustomerProfileCompletion.Evaluate(
            Customer(displayName: "Asha Rao", email: "asha@example.com"));

        Assert.False(completion.IsComplete);
        Assert.Equal([CustomerProfileCompletion.FieldPhone], completion.MissingFields);
        Assert.Equal(67, completion.PercentComplete);
    }

    [Fact]
    public void Fresh_phone_signup_is_missing_name_and_email()
    {
        var completion = CustomerProfileCompletion.Evaluate(Customer(phone: "+919876543210"));

        Assert.False(completion.IsComplete);
        Assert.Equal(
            [CustomerProfileCompletion.FieldName, CustomerProfileCompletion.FieldEmail],
            completion.MissingFields);
        Assert.Equal(33, completion.PercentComplete);
    }

    [Fact]
    public void A_first_name_alone_satisfies_the_name_field()
    {
        // Customers who only ever typed a first name should not be nagged forever.
        var completion = CustomerProfileCompletion.Evaluate(
            Customer(firstName: "Asha", email: "asha@example.com", phone: "+919876543210"));

        Assert.True(completion.IsComplete);
        Assert.Equal(100, completion.PercentComplete);
    }

    [Fact]
    public void Whitespace_only_values_do_not_count_as_filled_in()
    {
        var completion = CustomerProfileCompletion.Evaluate(
            Customer(displayName: "   ", email: "  ", phone: "+919876543210"));

        Assert.Equal(
            [CustomerProfileCompletion.FieldName, CustomerProfileCompletion.FieldEmail],
            completion.MissingFields);
    }

    [Fact]
    public void Empty_profile_reports_every_field_missing()
    {
        var completion = CustomerProfileCompletion.Evaluate(Customer());

        Assert.False(completion.IsComplete);
        Assert.Equal(3, completion.MissingFields.Count);
        Assert.Equal(0, completion.PercentComplete);
    }

    [Fact]
    public void IsComplete_shorthand_matches_the_full_evaluation()
    {
        var complete = Customer(displayName: "Asha Rao", email: "a@example.com", phone: "+919876543210");
        var incomplete = Customer(displayName: "Asha Rao");

        Assert.True(CustomerProfileCompletion.IsComplete(complete));
        Assert.False(CustomerProfileCompletion.IsComplete(incomplete));
    }
}
