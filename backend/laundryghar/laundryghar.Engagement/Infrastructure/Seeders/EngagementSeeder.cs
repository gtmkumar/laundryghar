using Microsoft.Extensions.Hosting;

namespace laundryghar.Engagement.Infrastructure.Seeders;

/// <summary>
/// Idempotent engagement/CMS seeder — Development only.
///
/// Seeds under LG-MAIN brand:
///   3 onboarding_slides (app_type=customer)
///   3 onboarding_slides (app_type=rider)
///   1 app_banner        (placement=home_top, status=active)
///   1 mobile_app_config for android (status=active)
///   1 mobile_app_config for ios     (status=active)
///   2 notification_templates (order_placed/sms, order_ready/whatsapp)
///
/// All enum values are verified against DB CHECK constraints:
///   onboarding_slides.app_type:  customer, rider, staff, pos
///   onboarding_slides.status:    active, inactive, archived
///   app_banners.placement:       home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile
///   app_banners.status:          active, inactive, archived
///   mobile_app_config.platform:  android, ios, web
///   mobile_app_config.status:    active, inactive, archived
///   notification_templates.channel: sms, whatsapp, email, push, in_app, voice
///   notification_templates.status:  active, inactive, archived
/// </summary>
public sealed class EngagementSeeder
{
    private readonly LaundryGharDbContext _db;
    private readonly IHostEnvironment     _env;
    private readonly ILogger<EngagementSeeder> _logger;

    public EngagementSeeder(
        LaundryGharDbContext db,
        IHostEnvironment env,
        ILogger<EngagementSeeder> logger)
    {
        _db     = db;
        _env    = env;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException(
                "EngagementSeeder may only run in Development. Use a controlled bootstrap process for other environments.");

        _logger.LogInformation("Running engagement/CMS seeder...");

        var brand = await _db.Brands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "LG-MAIN", ct);

        if (brand is null)
        {
            _logger.LogWarning("Brand LG-MAIN not found. Run Identity seeder first. Skipping engagement seed.");
            return;
        }

        var now     = DateTimeOffset.UtcNow;
        var brandId = brand.Id;

        await SeedOnboardingSlidesAsync(brandId, now, ct);
        await SeedAppBannersAsync(brandId, now, ct);
        await SeedMobileAppConfigsAsync(brandId, now, ct);
        await SeedNotificationTemplatesAsync(brandId, now, ct);

        _logger.LogInformation("Engagement/CMS seeding complete.");
    }

    // ── Onboarding Slides ──────────────────────────────────────────────────────

    private async Task SeedOnboardingSlidesAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        // Load existing by (brand_id, app_type, title)
        var existing = await _db.OnboardingSlides.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => new { x.AppType, x.Title })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.AppType, x.Title)).ToHashSet();

        int added = 0;

        void Ensure(string appType, string title, string titleLocalizedJson, string description,
            string descriptionLocalizedJson, string imageUrl, short order)
        {
            if (existingSet.Contains((appType, title))) return;

            _db.OnboardingSlides.Add(new OnboardingSlide
            {
                Id                   = Guid.NewGuid(),
                BrandId              = brandId,
                AppType              = appType,          // CHECK: customer, rider, staff, pos
                Title                = title,
                TitleLocalized       = titleLocalizedJson,
                Description          = description,
                DescriptionLocalized = descriptionLocalizedJson,
                ImageUrl             = imageUrl,
                DisplayOrder         = order,
                IsActive             = true,
                Status               = "active",         // CHECK: active, inactive, archived
                CreatedAt            = now,
                UpdatedAt            = now,
            });
            existingSet.Add((appType, title));
            added++;
        }

        // Customer slides
        Ensure("customer", "Welcome to Laundry Ghar",
            "{\"en\":\"Welcome to Laundry Ghar\",\"hi\":\"Laundry Ghar में आपका स्वागत है\"}",
            "Premium laundry service at your doorstep.",
            "{\"en\":\"Premium laundry service at your doorstep.\",\"hi\":\"आपके दरवाजे पर प्रीमियम लॉन्ड्री सेवा।\"}",
            "https://cdn.laundryghar.com/onboarding/customer-welcome.png", 1);

        Ensure("customer", "Schedule a Pickup",
            "{\"en\":\"Schedule a Pickup\",\"hi\":\"पिकअप शेड्यूल करें\"}",
            "Book a convenient pickup slot from the app.",
            "{\"en\":\"Book a convenient pickup slot from the app.\",\"hi\":\"ऐप से सुविधाजनक पिकअप स्लॉट बुक करें।\"}",
            "https://cdn.laundryghar.com/onboarding/customer-pickup.png", 2);

        Ensure("customer", "Track Your Order",
            "{\"en\":\"Track Your Order\",\"hi\":\"अपना ऑर्डर ट्रैक करें\"}",
            "Real-time updates from pickup to delivery.",
            "{\"en\":\"Real-time updates from pickup to delivery.\",\"hi\":\"पिकअप से डिलीवरी तक रियल-टाइम अपडेट।\"}",
            "https://cdn.laundryghar.com/onboarding/customer-track.png", 3);

        // Rider slides
        Ensure("rider", "Welcome Rider",
            "{\"en\":\"Welcome Rider\"}",
            "Manage your pickups and deliveries efficiently.",
            "{\"en\":\"Manage your pickups and deliveries efficiently.\"}",
            "https://cdn.laundryghar.com/onboarding/rider-welcome.png", 1);

        Ensure("rider", "View Assigned Orders",
            "{\"en\":\"View Assigned Orders\"}",
            "See all your assigned pickup and delivery tasks.",
            "{\"en\":\"See all your assigned pickup and delivery tasks.\"}",
            "https://cdn.laundryghar.com/onboarding/rider-orders.png", 2);

        Ensure("rider", "Update Delivery Status",
            "{\"en\":\"Update Delivery Status\"}",
            "Mark orders picked up, in-transit, or delivered.",
            "{\"en\":\"Mark orders picked up, in-transit, or delivered.\"}",
            "https://cdn.laundryghar.com/onboarding/rider-status.png", 3);

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} onboarding slides.", added);
        }
        else
        {
            _logger.LogInformation("Onboarding slides already seeded. Skipping.");
        }
    }

    // ── App Banners ────────────────────────────────────────────────────────────

    private async Task SeedAppBannersAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        const string BannerTitle = "Grand Launch Offer";

        var exists = await _db.AppBanners.IgnoreQueryFilters()
            .AnyAsync(x => x.BrandId == brandId && x.Title == BannerTitle && x.Placement == "home_top", ct);
        if (exists)
        {
            _logger.LogInformation("App banners already seeded. Skipping.");
            return;
        }

        _db.AppBanners.Add(new AppBanner
        {
            Id               = Guid.NewGuid(),
            BrandId          = brandId,
            AppType          = "customer",
            Placement        = "home_top",      // CHECK: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile
            Title            = BannerTitle,
            TitleLocalized   = "{\"en\":\"Grand Launch Offer\",\"hi\":\"ग्रैंड लॉन्च ऑफर\"}",
            Subtitle         = "Get 20% off on your first order!",
            SubtitleLocalized = "{\"en\":\"Get 20% off on your first order!\",\"hi\":\"पहले ऑर्डर पर 20% की छूट पाएं!\"}",
            ImageUrl         = "https://cdn.laundryghar.com/banners/grand-launch.png",
            CtaText          = "Order Now",
            CtaDeeplink      = "laundryghar://orders/new",
            DisplayOrder     = 1,
            IsActive         = true,
            ImpressionsCount = 0,
            ClicksCount      = 0,
            Status           = "active",        // CHECK: active, inactive, archived
            CreatedAt        = now,
            UpdatedAt        = now,
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded app banner (home_top).");
    }

    // ── Mobile App Config ──────────────────────────────────────────────────────

    private async Task SeedMobileAppConfigsAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.MobileAppConfigs.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => new { x.Platform, x.ConfigKey })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.Platform, x.ConfigKey)).ToHashSet();

        int added = 0;

        void Ensure(string platform, string configKey, string configValueJson,
            string description, string minVersion, bool isForceUpdate)
        {
            if (existingSet.Contains((platform, configKey))) return;
            _db.MobileAppConfigs.Add(new MobileAppConfig
            {
                Id            = Guid.NewGuid(),
                BrandId       = brandId,
                AppType       = "customer",
                Platform      = platform,         // CHECK: android, ios, web
                ConfigKey     = configKey,
                ConfigValue   = configValueJson,
                Description   = description,
                IsForceUpdate = isForceUpdate,
                MinAppVersion = minVersion,
                RolloutPercent = 100,
                IsActive      = true,
                Status        = "active",         // CHECK: active, inactive, archived
                CreatedAt     = now,
                UpdatedAt     = now,
            });
            existingSet.Add((platform, configKey));
            added++;
        }

        Ensure("android", "app_settings",
            "{\"min_version\":\"1.0.0\",\"force_update_version\":\"0.9.0\",\"maintenance_mode\":false,\"feature_flags\":{\"new_checkout\":true}}",
            "Core Android app runtime settings", "1.0.0", false);

        Ensure("ios", "app_settings",
            "{\"min_version\":\"1.0.0\",\"force_update_version\":\"0.9.0\",\"maintenance_mode\":false,\"feature_flags\":{\"new_checkout\":true}}",
            "Core iOS app runtime settings", "1.0.0", false);

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} mobile app config rows.", added);
        }
        else
        {
            _logger.LogInformation("Mobile app configs already seeded. Skipping.");
        }
    }

    // ── Notification Templates ─────────────────────────────────────────────────

    private async Task SeedNotificationTemplatesAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.NotificationTemplates.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => x.Code)
            .ToHashSetAsync(ct);

        int added = 0;

        void Ensure(string code, string name, string channel, string category, string locale,
            string body, string? subject, bool isTransactional)
        {
            if (existing.Contains(code)) return;
            _db.NotificationTemplates.Add(new NotificationTemplate
            {
                Id              = Guid.NewGuid(),
                BrandId         = brandId,
                Code            = code,
                Name            = name,
                Channel         = channel,          // CHECK: sms, whatsapp, email, push, in_app, voice
                Category        = category,
                Locale          = locale,
                BodyTemplate    = body,
                SubjectTemplate = subject,
                Variables       = "[]",
                VersionNumber   = 1,
                IsTransactional = isTransactional,
                IsActive        = true,
                Status          = "active",          // CHECK: active, inactive, archived
                CreatedAt       = now,
                UpdatedAt       = now,
            });
            existing.Add(code);
            added++;
        }

        // ── Existing templates (preserved) ────────────────────────────────────
        // order_placed — SMS
        Ensure(
            code: "ORDER_PLACED_SMS",
            name: "Order Placed - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been placed. We'll pick it up on {{pickup_date}}. Track: {{tracking_url}}",
            subject: null,
            isTransactional: true);

        // order_ready — WhatsApp
        Ensure(
            code: "ORDER_READY_WHATSAPP",
            name: "Order Ready for Pickup - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your order #{{order_number}} is ready! We'll deliver it on {{delivery_date}}. Thank you for choosing Laundry Ghar.",
            subject: null,
            isTransactional: true);

        // ── Lifecycle templates — WhatsApp (utility/transactional) ─────────────
        Ensure(
            code: "ORDER_PICKUP_SCHEDULED_WHATSAPP",
            name: "Order Pickup Scheduled - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} pickup is scheduled for {{pickup_date}}. Our rider will be at your door soon!",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_PICKED_UP_WHATSAPP",
            name: "Order Picked Up - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, we've picked up your order #{{order_number}}. Your clothes are on their way to our laundry facility. We'll keep you updated!",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_OUT_FOR_DELIVERY_WHATSAPP",
            name: "Order Out for Delivery - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your order #{{order_number}} is out for delivery! Our rider is on the way. Estimated delivery: {{delivery_date}}.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_DELIVERED_WHATSAPP",
            name: "Order Delivered - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been delivered! Thank you for choosing us. Rate your experience: {{tracking_url}}",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_CANCELLED_WHATSAPP",
            name: "Order Cancelled - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, your Laundry Ghar order #{{order_number}} has been cancelled. If you have questions please contact our support. We're sorry for the inconvenience.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "PAYMENT_CAPTURED_WHATSAPP",
            name: "Payment Received - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, we've received your payment of ₹{{amount}} for order #{{order_number}}. Thank you!",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "REFUND_INITIATED_WHATSAPP",
            name: "Refund Initiated - WhatsApp",
            channel: "whatsapp",
            category: "transactional",
            locale: "en",
            body: "Hi {{customer_name}}, a refund of ₹{{amount}} for order #{{order_number}} has been initiated. It will reflect in your account within 5-7 business days.",
            subject: null,
            isTransactional: true);

        // ── Lifecycle templates — SMS (fallback when WhatsApp not opted-in) ────
        Ensure(
            code: "ORDER_PICKUP_SCHEDULED_SMS",
            name: "Order Pickup Scheduled - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Order #{{order_number}} pickup scheduled for {{pickup_date}}. -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_PICKED_UP_SMS",
            name: "Order Picked Up - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Order #{{order_number}} picked up. We'll notify you when ready. -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_READY_SMS",
            name: "Order Ready - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Your order #{{order_number}} is ready for delivery on {{delivery_date}}. -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_OUT_FOR_DELIVERY_SMS",
            name: "Order Out for Delivery - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Order #{{order_number}} is out for delivery. ETA: {{delivery_date}}. -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_DELIVERED_SMS",
            name: "Order Delivered - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Order #{{order_number}} delivered! Thank you. Rate us: {{tracking_url}} -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_CANCELLED_SMS",
            name: "Order Cancelled - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Order #{{order_number}} has been cancelled. Contact support for assistance. -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "PAYMENT_CAPTURED_SMS",
            name: "Payment Received - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Payment of Rs.{{amount}} received for order #{{order_number}}. Thank you! -LaunGhar",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "REFUND_INITIATED_SMS",
            name: "Refund Initiated - SMS",
            channel: "sms",
            category: "transactional",
            locale: "en",
            body: "Laundry Ghar: Refund of Rs.{{amount}} for order #{{order_number}} initiated. Allow 5-7 days. -LaunGhar",
            subject: null,
            isTransactional: true);

        // ── Lifecycle templates — Push (second fallback) ───────────────────────
        Ensure(
            code: "ORDER_PICKUP_SCHEDULED_PUSH",
            name: "Order Pickup Scheduled - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Your order #{{order_number}} pickup is scheduled for {{pickup_date}}.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_PICKED_UP_PUSH",
            name: "Order Picked Up - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "We've picked up order #{{order_number}}. It's in our facility now!",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_READY_PUSH",
            name: "Order Ready - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Order #{{order_number}} is clean and ready for delivery on {{delivery_date}}.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_OUT_FOR_DELIVERY_PUSH",
            name: "Order Out for Delivery - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Order #{{order_number}} is on its way! ETA: {{delivery_date}}.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_DELIVERED_PUSH",
            name: "Order Delivered - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Order #{{order_number}} delivered! Fresh and clean. Thank you for choosing Laundry Ghar.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "ORDER_CANCELLED_PUSH",
            name: "Order Cancelled - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Your order #{{order_number}} has been cancelled.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "PAYMENT_CAPTURED_PUSH",
            name: "Payment Received - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Payment of ₹{{amount}} received for order #{{order_number}}.",
            subject: null,
            isTransactional: true);

        Ensure(
            code: "REFUND_INITIATED_PUSH",
            name: "Refund Initiated - Push",
            channel: "push",
            category: "transactional",
            locale: "en",
            body: "Refund of ₹{{amount}} initiated for order #{{order_number}}. Allow 5-7 business days.",
            subject: null,
            isTransactional: true);

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} notification templates.", added);
        }
        else
        {
            _logger.LogInformation("Notification templates already seeded. Skipping.");
        }
    }
}
