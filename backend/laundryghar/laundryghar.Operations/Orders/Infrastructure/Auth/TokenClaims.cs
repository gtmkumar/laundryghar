namespace laundryghar.Orders.Infrastructure.Auth;

/// <summary>System user JWT claims. token_use = "user". Pinned cross-service contract.</summary>
public sealed record TokenClaims { public const string TokenUseValue = "user"; }

/// <summary>Customer JWT claims. token_use = "customer". Pinned cross-service contract.</summary>
public sealed record CustomerTokenClaims { public const string TokenUseValue = "customer"; }
