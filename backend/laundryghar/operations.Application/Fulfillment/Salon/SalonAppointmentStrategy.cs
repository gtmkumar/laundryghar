using laundryghar.SharedDataModel.Enums;

namespace operations.Application.Fulfillment.Salon;

/// <summary>
/// The salon <c>appointment</c> fulfilment strategy: an on-site booked time slot against staff/
/// resource capacity, with NO pickup/delivery legs and NO wash/QC pipeline. Its state machine —
/// <c>booked → confirmed → checked_in → in_service → completed</c> — is a vocabulary entirely its
/// own (see <see cref="SalonStatus"/>), so it OVERRIDES <see cref="LifecycleStateFor"/> to map its
/// private statuses onto the shared neutral <c>OrderLifecycleState</c>. This is the Phase-4
/// validation that the Phase-1 seam supports a genuinely different vertical without touching the
/// shared spine.
/// </summary>
public sealed class SalonAppointmentStrategy : StateMachineStrategyBase
{
    public override string FulfillmentMode => laundryghar.SharedDataModel.Enums.FulfillmentMode.Appointment;
    public override string InitialStatus => SalonStatus.Booked;
    public override IReadOnlySet<string> TerminalStatuses => Terminals;

    protected override IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions => Map;
    protected override IReadOnlyList<string> HappyPath => Path;

    // No pickup or delivery leg — an appointment is performed on-site. PostPickupStatus is never
    // invoked for this mode (there is no rider pickup task); a sentinel keeps the contract total.
    public override string PostPickupStatus => SalonStatus.CheckedIn;
    public override bool RequiresStoreDrop => false;
    public override FulfilmentLegs ResolveLegs(bool requestedPickup, bool requestedDelivery)
        => new(RequiresPickup: false, RequiresDelivery: false);

    // Customer may cancel before service begins.
    public override bool CanCustomerCancel(string status)
        => status is SalonStatus.Booked or SalonStatus.Confirmed;

    // Salon owns its own vocabulary, so it cannot use the base OrderStatus→super-state map.
    public override string LifecycleStateFor(string status) => status switch
    {
        SalonStatus.Booked                                          => OrderLifecycleState.Created,
        SalonStatus.Confirmed or SalonStatus.CheckedIn
            or SalonStatus.InService                                => OrderLifecycleState.Active,
        SalonStatus.Completed                                       => OrderLifecycleState.Completed,
        SalonStatus.Cancelled or SalonStatus.NoShow                 => OrderLifecycleState.Cancelled,
        _                                                           => OrderLifecycleState.Active,
    };

    private static readonly IReadOnlySet<string> Terminals = new HashSet<string>
    {
        SalonStatus.Completed, SalonStatus.Cancelled, SalonStatus.NoShow,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [SalonStatus.Booked]    = new HashSet<string> { SalonStatus.Confirmed, SalonStatus.Cancelled },
            [SalonStatus.Confirmed] = new HashSet<string> { SalonStatus.CheckedIn, SalonStatus.Cancelled, SalonStatus.NoShow },
            [SalonStatus.CheckedIn] = new HashSet<string> { SalonStatus.InService, SalonStatus.Cancelled },
            [SalonStatus.InService] = new HashSet<string> { SalonStatus.Completed },
            [SalonStatus.Completed] = new HashSet<string>(),
            [SalonStatus.Cancelled] = new HashSet<string>(),
            [SalonStatus.NoShow]    = new HashSet<string>(),
        };

    private static readonly string[] Path =
    [
        SalonStatus.Booked,
        SalonStatus.Confirmed,
        SalonStatus.CheckedIn,
        SalonStatus.InService,
        SalonStatus.Completed,
    ];
}
