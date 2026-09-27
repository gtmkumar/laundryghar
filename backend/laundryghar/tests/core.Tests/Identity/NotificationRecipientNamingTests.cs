using System.Reflection;
using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace core.Tests.Identity;

/// <summary>
/// Audit finding A-3, "naming drift": <c>UserType</c> spelled the role <c>franchise_owner</c> while
/// <c>NotificationRecipientType</c> spelled the same real person <c>franchisee</c>.
///
/// <para>It reached users. admin-web's Notification Outbox and Logs tabs render
/// <c>recipientType</c> straight into their Recipient column when a recipient has no phone or email,
/// so an operator could read "franchisee" on one screen and "franchise_owner" on every other.</para>
///
/// <para>Aligned onto <c>franchise_owner</c> — the spelling that is load-bearing in
/// <c>users.user_type</c>, <c>roles.code</c>, the JWT claims and their CHECK constraints — with
/// migration 0033 moving the notifications CHECK with it.</para>
/// </summary>
public sealed class NotificationRecipientNamingTests
{
    [Fact]
    public void The_notification_recipient_and_the_user_type_name_the_same_person_the_same_way()
    {
        Assert.Equal(UserType.FranchiseOwner, NotificationRecipientType.FranchiseOwner);
    }

    [Fact]
    public void No_notification_recipient_constant_still_spells_it_franchisee()
    {
        // Written over the type rather than over the one constant, so re-adding the old spelling
        // under any name fails too.
        var values = typeof(NotificationRecipientType)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.DoesNotContain("franchisee", values);
        Assert.Contains("franchise_owner", values);
    }

    [Fact]
    public void Every_recipient_type_that_names_a_staff_person_matches_a_real_user_type()
    {
        // 'customer' and 'manual' are deliberately not user types — a customer lives in a separate
        // identity space and 'manual' is an ad-hoc address. The rest name staff, and a staff-shaped
        // value that no UserType recognises is exactly the drift this finding was.
        string[] notStaff = ["customer", "manual"];

        var staffValues = typeof(NotificationRecipientType)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => !notStaff.Contains(v) && v != "user");   // 'user' is the generic staff case

        foreach (var v in staffValues)
            Assert.True(UserType.IsValid(v), $"Recipient type '{v}' matches no UserType.");
    }
}
