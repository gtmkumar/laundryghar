using System.Security.Cryptography;
using System.Text;
using laundryghar.Commerce.Application.Webhooks;

namespace laundryghar.Commerce.Tests.Webhooks;

/// <summary>
/// DEF-2 regression: the Razorpay webhook handler must bind BOTH real snake_case
/// payloads (order_id, error_code) and the dev/legacy camelCase payloads (orderId,
/// errorCode). It must also reject tampered HMAC signatures.
///
/// White-box: ParseEvent / VerifyHmac are internal (InternalsVisibleTo Commerce.Tests).
/// </summary>
public sealed class RazorpayWebhookParseTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ── snake_case (real Razorpay) ─────────────────────────────────────────────

    [Fact]
    public void ParseEvent_BindsSnakeCasePayload()
    {
        const string json = """
        {
          "event": "payment.captured",
          "payload": {
            "payment": {
              "entity": {
                "id": "pay_SNAKE001",
                "order_id": "order_SNAKE001",
                "error_code": null,
                "error_description": null
              }
            }
          }
        }
        """;

        var evt = ProcessRazorpayWebhookHandler.ParseEvent(Utf8(json));

        Assert.NotNull(evt);
        Assert.Equal("payment.captured", evt!.Event);
        var entity = evt.Payload?.Payment?.Entity;
        Assert.NotNull(entity);
        Assert.Equal("pay_SNAKE001", entity!.Id);
        Assert.Equal("order_SNAKE001", entity.OrderId);
    }

    [Fact]
    public void ParseEvent_BindsSnakeCaseErrorFields()
    {
        const string json = """
        {
          "event": "payment.failed",
          "payload": {
            "payment": {
              "entity": {
                "id": "pay_FAIL001",
                "order_id": "order_FAIL001",
                "error_code": "BAD_REQUEST_ERROR",
                "error_description": "Payment failed at gateway"
              }
            }
          }
        }
        """;

        var evt = ProcessRazorpayWebhookHandler.ParseEvent(Utf8(json));

        var entity = evt!.Payload!.Payment!.Entity!;
        Assert.Equal("BAD_REQUEST_ERROR", entity.ErrorCode);
        Assert.Equal("Payment failed at gateway", entity.ErrorDescription);
    }

    // ── camelCase (dev gateway / legacy) still works ───────────────────────────

    [Fact]
    public void ParseEvent_BindsCamelCasePayload()
    {
        const string json = """
        {
          "event": "payment.failed",
          "payload": {
            "payment": {
              "entity": {
                "id": "pay_CAMEL001",
                "orderId": "order_CAMEL001",
                "errorCode": "GATEWAY_ERROR",
                "errorDescription": "camel failure"
              }
            }
          }
        }
        """;

        var evt = ProcessRazorpayWebhookHandler.ParseEvent(Utf8(json));

        var entity = evt!.Payload!.Payment!.Entity!;
        Assert.Equal("pay_CAMEL001", entity.Id);
        Assert.Equal("order_CAMEL001", entity.OrderId);
        Assert.Equal("GATEWAY_ERROR", entity.ErrorCode);
        Assert.Equal("camel failure", entity.ErrorDescription);
    }

    // ── HMAC verification ──────────────────────────────────────────────────────

    private static string ComputeSignature(byte[] body, string secret)
    {
        var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    [Fact]
    public void VerifyHmac_AcceptsValidSignature()
    {
        var body   = Utf8("""{"event":"payment.captured"}""");
        const string secret = "whsec_test_123";
        var sig    = ComputeSignature(body, secret);

        Assert.True(ProcessRazorpayWebhookHandler.VerifyHmac(body, sig, secret));
    }

    [Fact]
    public void VerifyHmac_RejectsTamperedSignature()
    {
        var body   = Utf8("""{"event":"payment.captured"}""");
        const string secret = "whsec_test_123";
        var sig    = ComputeSignature(body, secret);
        var tampered = sig[..^1] + (sig[^1] == 'a' ? 'b' : 'a');

        Assert.False(ProcessRazorpayWebhookHandler.VerifyHmac(body, tampered, secret));
    }

    [Fact]
    public void VerifyHmac_RejectsMissingSignature()
    {
        var body = Utf8("""{"event":"payment.captured"}""");
        Assert.False(ProcessRazorpayWebhookHandler.VerifyHmac(body, null, "whsec_test_123"));
        Assert.False(ProcessRazorpayWebhookHandler.VerifyHmac(body, "", "whsec_test_123"));
    }

    [Fact]
    public void VerifyHmac_RejectsSignatureFromWrongSecret()
    {
        var body = Utf8("""{"event":"payment.captured"}""");
        var sig  = ComputeSignature(body, "wrong_secret");
        Assert.False(ProcessRazorpayWebhookHandler.VerifyHmac(body, sig, "actual_secret"));
    }
}
