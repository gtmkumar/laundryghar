namespace core.Application.Common.Interfaces;

/// <summary>Seam for OTP dispatch. Dev implementation logs the code. Swap for MSG91 in production.</summary>
public interface IOtpSender
{
    Task SendAsync(string identifier, string identifierType, string plainCode, string purpose, CancellationToken ct = default, Guid? brandId = null);
}
