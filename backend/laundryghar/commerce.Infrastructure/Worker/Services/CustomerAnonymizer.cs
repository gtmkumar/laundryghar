namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Stateless, pure anonymization logic for the DPDP erasure pipeline.
/// Extracted from <see cref="CustomerErasureService"/> so it can be unit-tested
/// without starting the hosted service.
///
/// All methods operate on in-memory entity instances — no DB calls, no I/O.
/// </summary>
public static class CustomerAnonymizer
{
    /// <summary>
    /// Rewrites PII fields on a Customer entity in memory.
    /// Does NOT call SaveChanges — callers are responsible for persisting.
    ///
    /// Field map (old → tombstone):
    ///   first_name    → "Deleted"
    ///   last_name     → "User"
    ///   display_name  → "Deleted User"
    ///   phone_e164    → "+00del{tombstoneId}"   (reserved CC +00, unique per customer, fits varchar(20))
    ///   email         → "deleted+{tombstoneId}@anon.invalid"
    ///   gender        → NULL
    ///   date_of_birth → NULL
    ///   avatar_url    → NULL
    ///   *_opt_in      → false (all 5 channels)
    ///   phone_verified_at / email_verified_at → NULL
    ///   status        → "deleted"   (satisfies customers_status_check)
    ///   deleted_at    → now
    ///   updated_at    → now
    ///   version       → version + 1
    ///
    /// Intentionally NOT wiped (GST 72-month retention):
    ///   lifetime_orders, lifetime_spend, wallet_balance, loyalty_points_balance,
    ///   first_order_at, last_order_at, created_at, brand_id, customer_code, id.
    /// </summary>
    /// <param name="customer">The customer entity to anonymize.</param>
    /// <param name="tombstoneId">12-character hex suffix derived from the customer GUID (customer.Id.ToString("N")[..12]).</param>
    /// <param name="now">Timestamp to stamp on updated_at and deleted_at.</param>
    public static void Anonymize(
        laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer customer,
        string tombstoneId,
        DateTimeOffset now)
    {
        // Name fields
        customer.FirstName   = "Deleted";
        customer.LastName    = "User";
        customer.DisplayName = "Deleted User";

        // Phone: +00 is a reserved ITU country code — cannot be a real E.164 number.
        // "+00del" (6) + 12 hex chars = 18 chars total, fits phone_e164 varchar(20).
        // Uniqueness within the brand+phone unique index is guaranteed by the tombstoneId.
        customer.PhoneE164 = $"+00del{tombstoneId}";

        // Email: clearly synthetic, unique, satisfies brand+email unique index.
        customer.Email = $"deleted+{tombstoneId}@anon.invalid";

        // Biographic data
        customer.Gender      = null;
        customer.DateOfBirth = null;
        customer.AvatarUrl   = null;

        // Notification opt-ins — all off
        customer.MarketingOptIn = false;
        customer.SmsOptIn       = false;
        customer.WhatsappOptIn  = false;
        customer.EmailOptIn     = false;
        customer.PushOptIn      = false;

        // Verification timestamps
        customer.PhoneVerifiedAt = null;
        customer.EmailVerifiedAt = null;

        // Terminal status — satisfies customers_status_check: active|blocked|deletion_requested|deleted
        customer.Status    = "deleted";
        customer.DeletedAt = now;

        // Audit
        customer.UpdatedAt = now;
        customer.Version++;
    }
}
