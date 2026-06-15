using System.Text.Json.Serialization;

namespace core.Application.Identity.Settings;

/// <summary>
/// Outbound SMTP config as stored in <c>kernel.system_settings</c>
/// (category <c>email</c>, key <c>smtp</c>). Mirrors the JSON shape the
/// Settings UI reads and writes.
/// </summary>
public sealed class EmailSettings
{
    [JsonPropertyName("enabled")]   public bool Enabled { get; set; }
    [JsonPropertyName("host")]      public string Host { get; set; } = "";
    [JsonPropertyName("port")]      public int Port { get; set; } = 465;
    [JsonPropertyName("secure")]    public bool Secure { get; set; } = true;
    [JsonPropertyName("username")]  public string Username { get; set; } = "";
    [JsonPropertyName("password")]  public string Password { get; set; } = "";
    [JsonPropertyName("fromEmail")] public string FromEmail { get; set; } = "";
    [JsonPropertyName("fromName")]  public string FromName { get; set; } = "Laundry Ghar";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail);
}
