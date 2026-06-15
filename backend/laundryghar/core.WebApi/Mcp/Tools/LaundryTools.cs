using core.WebApi.Mcp.Infrastructure.Http;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace core.WebApi.Mcp.Tools;

/// <summary>
/// MCP tool class for the LaundryGhar customer-facing assistant.
/// All tools are READ-ONLY (Phase 1 spike scope).
/// The class is instantiated per-request by the MCP SDK via DI; HttpClient instances
/// are injected and carry the inbound customer bearer token via TokenForwardingHandler.
/// </summary>
[McpServerToolType]
public sealed class LaundryTools
{
    private readonly HttpClient _catalog;
    private readonly HttpClient _orders;
    private readonly ILogger<LaundryTools> _logger;

    public LaundryTools(
        [FromKeyedServices(DownstreamClientNames.Catalog)] HttpClient catalog,
        [FromKeyedServices(DownstreamClientNames.Orders)] HttpClient orders,
        ILogger<LaundryTools> logger)
    {
        _catalog = catalog;
        _orders = orders;
        _logger = logger;
    }

    // ── Tool 1: check_serviceability ─────────────────────────────────────────

    [McpServerTool(Name = "check_serviceability", ReadOnly = true)]
    [Description(
        "Check whether LaundryGhar pickup and delivery is available at a given Indian pincode. " +
        "Pass a 6-digit pincode (e.g. \"110001\"). Returns a plain-text answer indicating " +
        "serviceable or not. Use this when the customer asks if their area is covered, " +
        "whether we deliver to their location, or before recommending they book a pickup.")]
    public async Task<string> CheckServiceabilityAsync(
        [Description("6-digit Indian postal code to check (digits only, e.g. \"110001\")")]
        string pincode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pincode) || pincode.Length != 6 || !pincode.All(char.IsDigit))
            return "Invalid pincode. Please provide exactly 6 digits.";

        try
        {
            var response = await _catalog.GetAsync(
                $"/api/v1/customer/serviceability?pincode={pincode}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Serviceability check failed with HTTP {Status}", response.StatusCode);
                return $"Could not check serviceability right now (HTTP {(int)response.StatusCode}).";
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var data = json?["data"];
            var serviceable = data?["serviceable"]?.GetValue<bool>() ?? false;

            return serviceable
                ? $"Yes, LaundryGhar pickup and delivery is available at pincode {pincode}."
                : $"Sorry, LaundryGhar does not currently service pincode {pincode}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking serviceability for pincode {Pincode}", pincode);
            return "An error occurred while checking serviceability. Please try again.";
        }
    }

    // ── Tool 2: get_price_list ───────────────────────────────────────────────

    [McpServerTool(Name = "get_price_list", ReadOnly = true)]
    [Description(
        "Retrieve the LaundryGhar price list. Fetches live categories, services, and per-item prices. " +
        "Optionally filter by category name (e.g. \"Dry Cleaning\", \"Wash & Fold\", \"Steam Iron\"). " +
        "Returns a readable summary with service name, garment/item name, price per unit, and currency. " +
        "Use when the customer asks about pricing, cost of a specific service, or what we charge for an item.")]
    public async Task<string> GetPriceListAsync(
        [Description("Optional category name to filter by (e.g. \"Dry Cleaning\"). Pass null or empty to return all categories.")]
        string? categoryName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Fetch categories, services, and price list in parallel
            var categoriesTask = _catalog.GetAsync("/api/v1/customer/catalog/categories", cancellationToken);
            var servicesTask = _catalog.GetAsync("/api/v1/customer/catalog/services", cancellationToken);
            var priceListTask = _catalog.GetAsync("/api/v1/customer/catalog/price-list", cancellationToken);

            await Task.WhenAll(categoriesTask, servicesTask, priceListTask);

            var categoriesResp = await categoriesTask;
            var servicesResp = await servicesTask;
            var priceListResp = await priceListTask;

            if (!categoriesResp.IsSuccessStatusCode || !servicesResp.IsSuccessStatusCode || !priceListResp.IsSuccessStatusCode)
                return "Could not retrieve the price list at this time. Please try again shortly.";

            var categoriesJson = await categoriesResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var servicesJson = await servicesResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var priceListJson = await priceListResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);

            var categories = categoriesJson?["data"]?.AsArray() ?? [];
            var services = servicesJson?["data"]?.AsArray() ?? [];
            var priceItems = priceListJson?["data"]?.AsArray() ?? [];

            // Build a category id→name lookup
            var catNames = categories
                .Where(c => c?["id"] is not null && c["name"] is not null)
                .ToDictionary(
                    c => c!["id"]!.GetValue<string>(),
                    c => c!["name"]!.GetValue<string>());

            // Build a service id→(name, categoryId) lookup
            var svcInfo = services
                .Where(s => s?["id"] is not null)
                .ToDictionary(
                    s => s!["id"]!.GetValue<string>(),
                    s => (
                        name: s!["name"]?.GetValue<string>() ?? "Unknown service",
                        categoryId: s["categoryId"]?.GetValue<string>() ?? ""
                    ));

            var lines = new List<string>();
            string? currentCategory = null;

            foreach (var item in priceItems)
            {
                var serviceId = item?["serviceId"]?.GetValue<string>() ?? "";
                var itemName = item?["itemName"]?.GetValue<string>() ?? item?["name"]?.GetValue<string>() ?? "Item";
                var price = item?["unitPrice"]?.GetValue<decimal>() ?? item?["price"]?.GetValue<decimal>() ?? 0m;
                var unit = item?["unit"]?.GetValue<string>() ?? "per piece";
                var currency = item?["currency"]?.GetValue<string>() ?? "INR";

                if (!svcInfo.TryGetValue(serviceId, out var svc)) continue;

                var catName = catNames.GetValueOrDefault(svc.categoryId, "General");

                // Apply optional category filter
                if (!string.IsNullOrWhiteSpace(categoryName) &&
                    !catName.Contains(categoryName, StringComparison.OrdinalIgnoreCase) &&
                    !svc.name.Contains(categoryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (catName != currentCategory)
                {
                    if (lines.Count > 0) lines.Add("");
                    lines.Add($"## {catName}");
                    currentCategory = catName;
                }

                lines.Add($"  {svc.name} — {itemName}: {currency} {price:F2} {unit}");
            }

            if (lines.Count == 0)
            {
                return string.IsNullOrWhiteSpace(categoryName)
                    ? "No pricing information is available right now."
                    : $"No pricing found for category \"{categoryName}\".";
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching price list (categoryName={CategoryName})", categoryName);
            return "An error occurred while retrieving the price list. Please try again.";
        }
    }

    // ── Tool 3: get_my_addresses ─────────────────────────────────────────────

    [McpServerTool(Name = "get_my_addresses", ReadOnly = true)]
    [Description(
        "Retrieve the customer's saved delivery and pickup addresses. " +
        "Returns each address with an id, label (e.g. Home, Work), a one-line summary, and whether it is the default. " +
        "Use when the customer wants to see their saved addresses, choose a pickup location, or confirm where an order will go.")]
    public async Task<string> GetMyAddressesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _catalog.GetAsync("/api/v1/customer/addresses", cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return "You must be signed in to view your addresses.";

            if (!response.IsSuccessStatusCode)
                return $"Could not retrieve your addresses right now (HTTP {(int)response.StatusCode}).";

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var data = json?["data"]?.AsArray() ?? [];

            if (data.Count == 0)
                return "You have no saved addresses yet. Add one in the LaundryGhar app to get started.";

            var lines = new List<string> { "Your saved addresses:" };
            foreach (var addr in data)
            {
                var id = addr?["id"]?.GetValue<string>() ?? "";
                var label = addr?["label"]?.GetValue<string>() ?? "Address";
                var line1 = addr?["line1"]?.GetValue<string>() ?? "";
                var line2 = addr?["line2"]?.GetValue<string>() ?? "";
                var city = addr?["city"]?.GetValue<string>() ?? "";
                var pincode = addr?["pincode"]?.GetValue<string>() ?? addr?["pin"]?.GetValue<string>() ?? "";
                var isDefault = addr?["isDefault"]?.GetValue<bool>() ?? false;

                var summary = string.Join(", ", new[] { line1, line2, city, pincode }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

                var defaultTag = isDefault ? " [Default]" : "";
                lines.Add($"• {label}{defaultTag}: {summary} (id: {id})");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching customer addresses");
            return "An error occurred while retrieving your addresses. Please try again.";
        }
    }

    // ── Tool 4: get_pickup_slots ─────────────────────────────────────────────

    [McpServerTool(Name = "get_pickup_slots", ReadOnly = true)]
    [Description(
        "Retrieve available pickup and delivery time slots. " +
        "Pass an optional date in YYYY-MM-DD format; when omitted the tool returns slots for today and tomorrow. " +
        "Returns human-readable time windows with availability. " +
        "Use when the customer asks when they can schedule a pickup, what slots are free, " +
        "or before they choose a booking time.")]
    public async Task<string> GetPickupSlotsAsync(
        [Description("Date in YYYY-MM-DD format (e.g. \"2025-11-15\"). Omit to check today and tomorrow.")]
        string? date = null,
        CancellationToken cancellationToken = default)
    {
        // Default: today + tomorrow in IST (India Standard Time)
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        IEnumerable<DateOnly> datesToCheck;
        if (!string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var parsedDate))
        {
            datesToCheck = [parsedDate];
        }
        else
        {
            var today = DateOnly.FromDateTime(nowIst);
            datesToCheck = [today, today.AddDays(1)];
        }

        var allLines = new List<string>();

        foreach (var d in datesToCheck)
        {
            try
            {
                var response = await _orders.GetAsync(
                    $"/api/v1/customer/delivery-slots?date={d:yyyy-MM-dd}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    allLines.Add($"Slots for {d:dd MMM yyyy}: unavailable (HTTP {(int)response.StatusCode}).");
                    continue;
                }

                var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                var slots = json?["data"]?.AsArray() ?? [];

                allLines.Add($"Slots for {d:ddd, dd MMM yyyy}:");

                if (slots.Count == 0)
                {
                    allLines.Add("  No slots available.");
                    continue;
                }

                foreach (var slot in slots)
                {
                    var label = slot?["label"]?.GetValue<string>()
                                 ?? slot?["name"]?.GetValue<string>()
                                 ?? "Slot";
                    var startTime = slot?["startTime"]?.GetValue<string>()
                                 ?? slot?["start"]?.GetValue<string>() ?? "";
                    var endTime = slot?["endTime"]?.GetValue<string>()
                                 ?? slot?["end"]?.GetValue<string>() ?? "";
                    var available = slot?["available"]?.GetValue<bool>()
                                 ?? slot?["isAvailable"]?.GetValue<bool>() ?? true;
                    var capacity = slot?["remainingCapacity"]?.GetValue<int>()
                                 ?? slot?["slotsLeft"]?.GetValue<int>();

                    var window = (startTime, endTime) switch
                    {
                        ({ Length: > 0 }, { Length: > 0 }) => $"{startTime} – {endTime}",
                        ({ Length: > 0 }, _) => startTime,
                        _ => label
                    };

                    var availTag = available ? "Available" : "Full";
                    var capTag = capacity.HasValue && available ? $" ({capacity} slots left)" : "";
                    allLines.Add($"  • {label}: {window} — {availTag}{capTag}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching delivery slots for {Date}", d);
                allLines.Add($"Slots for {d:dd MMM yyyy}: could not retrieve.");
            }

            allLines.Add("");
        }

        return allLines.Count == 0
            ? "No slot information is available right now."
            : string.Join("\n", allLines).TrimEnd();
    }

    // ── Tool 5: get_my_orders ────────────────────────────────────────────────

    [McpServerTool(Name = "get_my_orders", ReadOnly = true)]
    [Description(
        "Retrieve the customer's recent orders and active pickup requests. " +
        "Pass an optional limit (default 5, max 20) to control how many are returned. " +
        "Returns order number, status, total amount, and date for each order, plus any pending pickup requests. " +
        "Use when the customer asks about their order history, current order status, " +
        "or wants to know what is happening with their laundry.")]
    public async Task<string> GetMyOrdersAsync(
        [Description("Maximum number of recent orders to return (1–20). Defaults to 5.")]
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);

        try
        {
            // Fetch orders and pickup requests in parallel
            var ordersTask = _orders.GetAsync($"/api/v1/customer/orders?page=1&pageSize={limit}", cancellationToken);
            var pickupsTask = _orders.GetAsync("/api/v1/customer/pickup-requests?page=1&pageSize=5&status=pending", cancellationToken);

            await Task.WhenAll(ordersTask, pickupsTask);

            var ordersResp = await ordersTask;
            var pickupsResp = await pickupsTask;

            var lines = new List<string>();

            // Orders
            if (ordersResp.IsSuccessStatusCode)
            {
                var json = await ordersResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                var orders = json?["data"]?.AsArray() ?? [];

                if (orders.Count > 0)
                {
                    lines.Add($"Your {(orders.Count == 1 ? "most recent order" : $"last {orders.Count} orders")}:");
                    foreach (var order in orders)
                    {
                        var orderNo = order?["orderNumber"]?.GetValue<string>()
                                      ?? order?["order_number"]?.GetValue<string>() ?? "N/A";
                        var status = order?["status"]?.GetValue<string>() ?? "Unknown";
                        var total = order?["total"]?.GetValue<decimal>()
                                      ?? order?["totalAmount"]?.GetValue<decimal>() ?? 0m;
                        var currency = order?["currency"]?.GetValue<string>() ?? "INR";
                        var createdAt = order?["createdAt"]?.GetValue<string>()
                                      ?? order?["created_at"]?.GetValue<string>() ?? "";

                        var dateStr = DateTimeOffset.TryParse(createdAt, out var dt)
                            ? dt.ToString("dd MMM yyyy")
                            : createdAt;

                        var statusDisplay = FormatStatus(status);
                        lines.Add($"  • Order {orderNo} — {statusDisplay} — {currency} {total:F2} — {dateStr}");
                    }
                }
                else
                {
                    lines.Add("You have no recent orders.");
                }
            }
            else
            {
                lines.Add("Could not retrieve your orders right now.");
            }

            // Pickup requests
            if (pickupsResp.IsSuccessStatusCode)
            {
                var json = await pickupsResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                var pickups = json?["data"]?.AsArray() ?? [];

                if (pickups.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Active pickup requests:");
                    foreach (var pickup in pickups)
                    {
                        var refNo = pickup?["referenceNumber"]?.GetValue<string>()
                                     ?? pickup?["id"]?.GetValue<string>() ?? "N/A";
                        var status = pickup?["status"]?.GetValue<string>() ?? "Pending";
                        var scheduled = pickup?["scheduledDate"]?.GetValue<string>()
                                     ?? pickup?["pickupDate"]?.GetValue<string>() ?? "";
                        var slot = pickup?["slotLabel"]?.GetValue<string>()
                                     ?? pickup?["timeSlot"]?.GetValue<string>() ?? "";

                        var scheduledStr = DateTimeOffset.TryParse(scheduled, out var sdt)
                            ? sdt.ToString("dd MMM yyyy")
                            : scheduled;

                        var slotSuffix = !string.IsNullOrWhiteSpace(slot) ? $" ({slot})" : "";
                        lines.Add($"  • Pickup #{refNo} — {status} — {scheduledStr}{slotSuffix}");
                    }
                }
            }

            return lines.Count == 0
                ? "You have no recent orders or pickup requests."
                : string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders for customer");
            return "An error occurred while retrieving your orders. Please try again.";
        }
    }

    // ── Tool 6: book_pickup ──────────────────────────────────────────────────

    [McpServerTool(Name = "book_pickup", ReadOnly = false, Idempotent = true)]
    [Description(
        "Book a laundry pickup for the authenticated customer. " +
        "IMPORTANT: Before calling this tool, read the complete booking summary back to the " +
        "user — include the items list, pickup date, time window, address label, and payment method — " +
        "and get their EXPLICIT verbal confirmation. Only call this tool AFTER the user confirms. " +
        "Do NOT call speculatively or to 'check' — it creates a real booking. " +
        "Payment preference must be 'cod' (cash on delivery) or 'wallet' (LaundryGhar wallet). " +
        "Online card/UPI payment is not available via the assistant yet; inform the user if they request it. " +
        "An idempotencyKey should be generated by the caller (e.g. a UUID) and stored for the session; " +
        "re-sending the same key is safe and will return the existing booking rather than creating a duplicate.")]
    public async Task<string> BookPickupAsync(
        [Description("ID of the customer's saved address for pickup (use get_my_addresses to find it).")]
        string addressId,
        [Description("Pickup date in YYYY-MM-DD format (e.g. \"2025-12-01\").")]
        string pickupDate,
        [Description("Start of the pickup time window in HH:mm format (e.g. \"09:00\").")]
        string pickupWindowStart,
        [Description("End of the pickup time window in HH:mm format (e.g. \"11:00\").")]
        string pickupWindowEnd,
        [Description("List of items to launder. Each item has a label (garment description) and quantity.")]
        BookingItem[] items,
        [Description(
            "Payment method: 'cod' (cash on delivery) or 'wallet' (LaundryGhar wallet balance). " +
            "Do NOT pass 'upi', 'card', or 'online' — these are not supported via the assistant.")]
        string paymentPreference,
        [Description("A unique idempotency key (UUID or similar) to prevent duplicate bookings on retry. Required.")]
        string idempotencyKey,
        [Description("Optional slot ID from get_pickup_slots if the customer chose a specific time slot.")]
        string? slotId = null,
        [Description("Optional instructions or notes from the customer (e.g. \"Handle with care\").")]
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        // ── Input validation ─────────────────────────────────────────────────
        if (!Guid.TryParse(addressId, out _))
            return "Invalid addressId — must be a valid UUID. Use get_my_addresses to find the correct address ID.";

        var normalised = paymentPreference?.ToLowerInvariant() ?? "";
        if (normalised is not ("cod" or "wallet"))
            return normalised is "upi" or "card" or "online" or "upi-deferred"
                ? "Online payment (UPI/card) is not available via the AI assistant yet. " +
                  "Please choose 'cod' (cash on delivery) or 'wallet' instead."
                : $"Invalid payment preference '{paymentPreference}'. Use 'cod' or 'wallet'.";

        if (!DateOnly.TryParseExact(pickupDate, "yyyy-MM-dd", out var date))
            return "Invalid pickupDate — use YYYY-MM-DD format (e.g. \"2025-12-01\").";

        if (!TimeOnly.TryParseExact(pickupWindowStart, "HH:mm", out var windowStart))
            return "Invalid pickupWindowStart — use HH:mm format (e.g. \"09:00\").";

        if (!TimeOnly.TryParseExact(pickupWindowEnd, "HH:mm", out var windowEnd))
            return "Invalid pickupWindowEnd — use HH:mm format (e.g. \"11:00\").";

        if (items is not { Length: > 0 })
            return "At least one item is required to book a pickup.";

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return "An idempotency key is required to safely book a pickup.";

        // ── Build cart items from simplified BookingItem array ───────────────
        var cartItems = items.Select(i => new
        {
            serviceId = (string?)null,
            itemId = (string?)null,
            displayLabel = i.Label,
            quantity = i.Quantity,
            estimatedUnitPrice = (decimal?)null
        }).ToArray();

        // ── Build request body ───────────────────────────────────────────────
        var body = new
        {
            addressId = addressId,
            slotId = slotId,
            pickupDate = date.ToString("yyyy-MM-dd"),
            pickupWindowStart = windowStart.ToString("HH:mm:ss"),
            pickupWindowEnd = windowEnd.ToString("HH:mm:ss"),
            isExpress = false,
            estimatedItems = (int?)items.Sum(i => i.Quantity),
            estimatedAmount = (decimal?)null,
            servicesRequested = Array.Empty<string>(),
            customerNotes = customerNotes,
            cartItems = cartItems,
            paymentPreference = normalised,
            idempotencyKey = idempotencyKey.Trim(),
            channel = "mcp"
        };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customer/pickup-requests");
            request.Headers.Add("Idempotency-Key", idempotencyKey.Trim());
            request.Headers.Add("X-Channel", "mcp");
            request.Content = JsonContent.Create(body);

            var response = await _orders.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return "You must be signed in to book a pickup.";

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("book_pickup failed: HTTP {Status} — {Body}", response.StatusCode, errorBody);
                return $"Booking failed (HTTP {(int)response.StatusCode}). " +
                       "Please check your address and time slot, then try again.";
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var data = json?["data"];

            var requestNumber = data?["requestNumber"]?.GetValue<string>()
                             ?? data?["request_number"]?.GetValue<string>() ?? "N/A";
            var status = data?["status"]?.GetValue<string>() ?? "pending";

            // Friendly payment label
            var payLabel = normalised switch
            {
                "wallet" => "LaundryGhar wallet",
                "cod" => "cash on delivery",
                _ => normalised
            };

            // Was this a duplicate-safe replay?
            var wasExisting = response.StatusCode == HttpStatusCode.OK &&
                              !string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)
                                  ? " (returning your existing booking)"
                                  : "";

            return $"Pickup booked successfully{wasExisting}! " +
                   $"Booking reference: {requestNumber}. " +
                   $"Pickup scheduled for {date:ddd, dd MMM yyyy}, " +
                   $"{windowStart:HH:mm}–{windowEnd:HH:mm}. " +
                   $"Payment: {payLabel}. " +
                   "Our team will confirm the pickup shortly.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling book_pickup for address {AddressId}", addressId);
            return "An error occurred while booking your pickup. Please try again.";
        }
    }

    // ── Tool 7: cancel_order ─────────────────────────────────────────────────

    [McpServerTool(Name = "cancel_order", Destructive = true)]
    [Description(
        "Cancel an existing order for the authenticated customer. " +
        "IMPORTANT: This action is irreversible. " +
        "Always confirm with the user before calling — repeat the order number and ask explicitly: " +
        "'Are you sure you want to cancel order X?' " +
        "Do NOT call unless the user has clearly confirmed cancellation. " +
        "Accepts either a GUID order ID or an order number string (e.g. 'LG-2025-ABCD-000123'). " +
        "The state machine may reject cancellation if the order is already picked up or in process " +
        "— the tool will surface a clear message in that case.")]
    public async Task<string> CancelOrderAsync(
        [Description(
            "The order to cancel. Can be a GUID (e.g. \"3fa85f64-5717-4562-b3fc-2c963f66afa6\") " +
            "or an order number string (e.g. \"LG-2025-ABCD-000123\"). " +
            "Use get_my_orders to find the correct identifier.")]
        string orderIdOrNumber,
        [Description("Optional reason for cancellation (recommended for customer records).")]
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderIdOrNumber))
            return "Please provide an order ID or order number.";

        try
        {
            Guid orderId;

            // ── Resolve order number → GUID if needed ────────────────────────
            if (!Guid.TryParse(orderIdOrNumber, out orderId))
            {
                // Non-GUID: treat as order number, search orders list
                var listResp = await _orders.GetAsync(
                    "/api/v1/customer/orders?page=1&pageSize=50", cancellationToken);

                if (!listResp.IsSuccessStatusCode)
                    return "Could not look up your orders to resolve the order number. Please try again.";

                var listJson = await listResp.Content.ReadFromJsonAsync<JsonObject>(
                    cancellationToken: cancellationToken);
                var orders = listJson?["data"]?.AsArray() ?? [];

                var match = orders.FirstOrDefault(o =>
                    string.Equals(
                        o?["orderNumber"]?.GetValue<string>() ?? o?["order_number"]?.GetValue<string>(),
                        orderIdOrNumber.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (match is null)
                    return $"Could not find an order with number '{orderIdOrNumber}'. " +
                           "Use get_my_orders to see your current orders.";

                var idStr = match["id"]?.GetValue<string>() ?? "";
                if (!Guid.TryParse(idStr, out orderId))
                    return "Found the order but could not parse its ID. Please try using the GUID directly.";
            }

            // ── POST cancel ──────────────────────────────────────────────────
            var cancelBody = reason is not null ? new { reason } : null;
            var cancelResp = await _orders.PostAsJsonAsync(
                $"/api/v1/customer/orders/{orderId}/cancel", cancelBody, cancellationToken);

            if (cancelResp.StatusCode == HttpStatusCode.Unauthorized)
                return "You must be signed in to cancel an order.";

            if (cancelResp.StatusCode == HttpStatusCode.NotFound)
                return $"Order '{orderIdOrNumber}' was not found or does not belong to your account.";

            if (cancelResp.StatusCode == HttpStatusCode.UnprocessableEntity ||
                cancelResp.StatusCode == (HttpStatusCode)422)
            {
                // State machine rejection — surface the message from the API.
                var errJson = await cancelResp.Content.ReadFromJsonAsync<JsonObject>(
                    cancellationToken: cancellationToken);
                var errMsg = errJson?["detail"]?.GetValue<string>()
                          ?? errJson?["message"]?.GetValue<string>()
                          ?? "The order cannot be cancelled in its current status.";
                return $"Cancellation rejected: {errMsg} " +
                       "Contact support for orders that are already in progress.";
            }

            if (!cancelResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("cancel_order failed: HTTP {Status}", cancelResp.StatusCode);
                return $"Cancellation failed (HTTP {(int)cancelResp.StatusCode}). Please try again.";
            }

            var json = await cancelResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var data = json?["data"];
            var status = data?["status"]?.GetValue<string>() ?? "cancelled";
            var num = data?["orderNumber"]?.GetValue<string>()
                      ?? data?["order_number"]?.GetValue<string>() ?? orderIdOrNumber;

            return $"Order {num} has been successfully cancelled (status: {FormatStatus(status)}). " +
                   "If you paid via wallet, any eligible refund will be processed within 24 hours.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderIdOrNumber}", orderIdOrNumber);
            return "An error occurred while cancelling the order. Please try again.";
        }
    }

    // ── Tool 8: track_order ──────────────────────────────────────────────────

    [McpServerTool(Name = "track_order", ReadOnly = true)]
    [Description(
        "Get the current status and full status history of an order for the authenticated customer. " +
        "Accepts either a GUID order ID or an order number string. " +
        "Returns a concise human-readable timeline showing each status change and when it occurred. " +
        "Use when the customer asks 'where is my order?', 'what is the status of my laundry?', " +
        "or wants to see the progress of a specific order.")]
    public async Task<string> TrackOrderAsync(
        [Description(
            "The order to track. Can be a GUID or an order number string (e.g. \"LG-2025-ABCD-000123\"). " +
            "Use get_my_orders to find your order identifiers.")]
        string orderIdOrNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderIdOrNumber))
            return "Please provide an order ID or order number.";

        try
        {
            Guid orderId;

            // ── Resolve order number → GUID if needed ────────────────────────
            if (!Guid.TryParse(orderIdOrNumber, out orderId))
            {
                var listResp = await _orders.GetAsync(
                    "/api/v1/customer/orders?page=1&pageSize=50", cancellationToken);

                if (!listResp.IsSuccessStatusCode)
                    return "Could not look up your orders to resolve the order number. Please try again.";

                var listJson = await listResp.Content.ReadFromJsonAsync<JsonObject>(
                    cancellationToken: cancellationToken);
                var orders = listJson?["data"]?.AsArray() ?? [];

                var match = orders.FirstOrDefault(o =>
                    string.Equals(
                        o?["orderNumber"]?.GetValue<string>() ?? o?["order_number"]?.GetValue<string>(),
                        orderIdOrNumber.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (match is null)
                    return $"Could not find an order with number '{orderIdOrNumber}'. " +
                           "Use get_my_orders to see your recent orders.";

                var idStr = match["id"]?.GetValue<string>() ?? "";
                if (!Guid.TryParse(idStr, out orderId))
                    return "Found the order but could not parse its ID. Please try using the GUID directly.";
            }

            // ── Fetch order + tracking history in parallel ───────────────────
            var orderTask = _orders.GetAsync($"/api/v1/customer/orders/{orderId}", cancellationToken);
            var trackingTask = _orders.GetAsync($"/api/v1/customer/orders/{orderId}/tracking", cancellationToken);

            await Task.WhenAll(orderTask, trackingTask);

            var orderResp = await orderTask;
            var trackingResp = await trackingTask;

            if (orderResp.StatusCode == HttpStatusCode.NotFound)
                return $"Order '{orderIdOrNumber}' was not found or does not belong to your account.";

            if (!orderResp.IsSuccessStatusCode)
                return $"Could not retrieve order details (HTTP {(int)orderResp.StatusCode}). Please try again.";

            var orderJson = await orderResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var order = orderJson?["data"];
            var num = order?["orderNumber"]?.GetValue<string>()
                          ?? order?["order_number"]?.GetValue<string>() ?? orderIdOrNumber;
            var status = order?["status"]?.GetValue<string>() ?? "unknown";
            var total = order?["grandTotal"]?.GetValue<decimal>()
                          ?? order?["grand_total"]?.GetValue<decimal>() ?? 0m;
            var currency = order?["currencyCode"]?.GetValue<string>() ?? "INR";
            var placedAt = order?["placedAt"]?.GetValue<string>()
                          ?? order?["placed_at"]?.GetValue<string>() ?? "";

            var placedStr = DateTimeOffset.TryParse(placedAt, out var pdt)
                ? pdt.ToString("dd MMM yyyy, HH:mm")
                : placedAt;

            var lines = new List<string>
            {
                $"Order {num} — {FormatStatus(status)}",
                $"Placed: {placedStr}  |  Total: {currency} {total:F2}",
                ""
            };

            // ── Timeline ─────────────────────────────────────────────────────
            if (trackingResp.IsSuccessStatusCode)
            {
                var trackJson = await trackingResp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                var history = trackJson?["data"]?.AsArray() ?? [];

                if (history.Count > 0)
                {
                    lines.Add("Timeline:");
                    foreach (var entry in history)
                    {
                        var toStatus = entry?["toStatus"]?.GetValue<string>()
                                     ?? entry?["to_status"]?.GetValue<string>() ?? "";
                        var changedAt = entry?["changedAt"]?.GetValue<string>()
                                     ?? entry?["changed_at"]?.GetValue<string>() ?? "";
                        var entryReason = entry?["reason"]?.GetValue<string>();

                        var timeStr = DateTimeOffset.TryParse(changedAt, out var cdt)
                            ? cdt.ToString("dd MMM, HH:mm")
                            : changedAt;

                        var reasonSuffix = !string.IsNullOrWhiteSpace(entryReason)
                            ? $" ({entryReason})"
                            : "";

                        lines.Add($"  {timeStr}  →  {FormatStatus(toStatus)}{reasonSuffix}");
                    }
                }
                else
                {
                    lines.Add("No status history available yet.");
                }
            }
            else
            {
                lines.Add($"Current status: {FormatStatus(status)}");
                lines.Add("(Detailed tracking history unavailable right now.)");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking order {OrderIdOrNumber}", orderIdOrNumber);
            return "An error occurred while retrieving tracking information. Please try again.";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatStatus(string raw) => raw.ToLowerInvariant() switch
    {
        "pending" => "Pending",
        "confirmed" => "Confirmed",
        "placed" => "Order placed",
        "pickup_scheduled" => "Pickup scheduled",
        "pickup_assigned" => "Rider assigned for pickup",
        "pickup_in_progress" => "Pickup in progress",
        "picked_up" => "Picked up",
        "received" => "Received at warehouse",
        "at_warehouse" => "At warehouse",
        "sorting" => "Sorting in progress",
        "in_process" => "Laundry in process",
        "processing" => "Processing",
        "qc" => "Quality check",
        "ready" => "Ready for delivery",
        "delivery_scheduled" => "Delivery scheduled",
        "delivery_assigned" => "Rider assigned for delivery",
        "out_for_delivery" => "Out for delivery",
        "delivered" => "Delivered",
        "cancelled" => "Cancelled",
        "returned" => "Returned",
        "rewash" => "Rewash in progress",
        "disputed" => "Under dispute",
        "closed" => "Closed",
        _ => raw
    };
}

/// <summary>
/// A simplified item descriptor used by the book_pickup MCP tool.
/// Avoids exposing the internal catalog IDs — the customer just describes their garments.
/// </summary>
public sealed class BookingItem
{
    /// <summary>Human-readable garment or bundle label (e.g. "Shirt – Wash and Iron", "3 T-shirts").</summary>
    [Description("Human-readable garment label (e.g. \"Shirt – Wash and Iron\", \"Bedsheet\").")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Quantity of this item type, must be >= 1.</summary>
    [Description("Number of pieces of this garment type. Must be at least 1.")]
    public int Quantity { get; set; } = 1;
}
