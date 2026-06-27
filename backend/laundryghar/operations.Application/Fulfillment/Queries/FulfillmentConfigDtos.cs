namespace operations.Application.Fulfillment.Queries;

/// <summary>One stage in a fulfilment mode's happy path — the backend-driven tracking descriptor a
/// client renders (instead of hardcoding a laundry status ladder). (Phase 3.)</summary>
public sealed record FulfillmentStageDto(
    string Status,          // strategy-owned detailed status (e.g. "in_service")
    string Label,           // humanised label ("In Service")
    int    Order,           // position in the happy path
    string LifecycleState   // the neutral super-state this maps to (created/active/completed/…)
);

/// <summary>The client-consumable configuration of one fulfilment mode — drives mobile/web
/// "backend-driven tracking", POS <c>useFulfillmentConfig</c>, and the admin status ladder, so a
/// new vertical needs no client status enums. (Phase 3, consumes the Phase-1 strategy seam.)</summary>
public sealed record FulfillmentConfigDto(
    string FulfillmentMode,
    string InitialStatus,
    IReadOnlyList<FulfillmentStageDto> Stages,
    IReadOnlyList<string> TerminalStatuses,
    bool RequiresStoreDrop,
    bool RequiresPickup,
    bool RequiresDelivery
);
