namespace laundryghar.SharedDataModel.Common;

/// <summary>
/// Dispatch mode configuration, persisted as JSON in kernel.system_settings
/// (category 'dispatch', key 'mode'). Resolved per-job with franchise &gt; brand &gt;
/// platform precedence.
///
/// Two modes:
///   push          — auto-assign the best-ranked rider immediately (current behaviour, default).
///   offer_accept   — offer to the best-ranked rider(s); they accept or the offer expires and
///                    re-offers to the next, falling back to push after MaxOfferRounds.
///
/// Policy: 'offer_accept' is a PLATFORM-level opt-in only. A franchise-scoped row may
/// only narrow to 'push' (never enable offer_accept) — enforced at the write path by the
/// 'dispatch.mode.manage' permission and by <see cref="Normalize"/>.
/// </summary>
public sealed class DispatchSettings
{
    public const string ModePush = "push";
    public const string ModeOfferAccept = "offer_accept";

    public string Mode { get; set; } = ModePush;

    /// <summary>Seconds an offer stays live before it expires and re-offers.</summary>
    public int OfferTtlSeconds { get; set; } = 60;

    /// <summary>How many offer rounds before falling back to push-assign.</summary>
    public int MaxOfferRounds { get; set; } = 3;

    /// <summary>How many riders to offer to per round.</summary>
    public int OffersPerRound { get; set; } = 1;

    public bool IsOfferAccept => Mode == ModeOfferAccept;

    /// <summary>
    /// Returns the effective mode given platform vs franchise scope. A franchise override
    /// may only force 'push'; it can never enable 'offer_accept'. Unknown modes fall back
    /// to 'push' (safe default).
    /// </summary>
    public static string Normalize(string? mode, bool isPlatformScope)
    {
        if (mode == ModeOfferAccept && isPlatformScope) return ModeOfferAccept;
        return ModePush;
    }
}
