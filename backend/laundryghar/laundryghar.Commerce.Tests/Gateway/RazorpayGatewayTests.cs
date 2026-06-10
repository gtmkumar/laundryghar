using System.Security.Cryptography;
using System.Text;
using laundryghar.Commerce.Infrastructure.Gateway;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace laundryghar.Commerce.Tests.Gateway;

/// <summary>
/// Unit tests for RazorpayPaymentGateway.VerifySignatureAsync.
/// No HTTP calls are made — signature verification is a local HMAC operation.
/// </summary>
public sealed class RazorpaySignatureTests
{
    private static RazorpayPaymentGateway BuildGateway(string keySecret)
    {
        var settings = new RazorpaySettings
        {
            KeyId     = "rzp_test_key",
            KeySecret = keySecret
        };

        var httpFactory = new Mock<IHttpClientFactory>();
        var opts        = Options.Create(settings);
        var logger      = NullLogger<RazorpayPaymentGateway>.Instance;

        return new RazorpayPaymentGateway(httpFactory.Object, opts, logger);
    }

    private static string ComputeExpectedSignature(string gatewayOrderId, string gatewayPaymentId, string keySecret)
    {
        var payload  = $"{gatewayOrderId}|{gatewayPaymentId}";
        var keyBytes = Encoding.UTF8.GetBytes(keySecret);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        var hmac     = HMACSHA256.HashData(keyBytes, msgBytes);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    [Fact]
    public async Task VerifySignatureAsync_WithCorrectSignature_ReturnsTrue()
    {
        const string keySecret       = "test_secret_abc123";
        const string gatewayOrderId  = "order_TESTORDER001";
        const string gatewayPaymentId = "pay_TESTPAY001";

        var signature = ComputeExpectedSignature(gatewayOrderId, gatewayPaymentId, keySecret);
        var gateway   = BuildGateway(keySecret);

        var result = await gateway.VerifySignatureAsync(gatewayOrderId, gatewayPaymentId, signature);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifySignatureAsync_WithWrongSecret_ReturnsFalse()
    {
        const string keySecret         = "test_secret_abc123";
        const string wrongSecret       = "wrong_secret_xyz";
        const string gatewayOrderId    = "order_TESTORDER001";
        const string gatewayPaymentId  = "pay_TESTPAY001";

        // Signature computed with the WRONG secret
        var signature = ComputeExpectedSignature(gatewayOrderId, gatewayPaymentId, wrongSecret);
        var gateway   = BuildGateway(keySecret);

        var result = await gateway.VerifySignatureAsync(gatewayOrderId, gatewayPaymentId, signature);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifySignatureAsync_WithTamperedOrderId_ReturnsFalse()
    {
        const string keySecret         = "test_secret_abc123";
        const string gatewayOrderId    = "order_TESTORDER001";
        const string gatewayPaymentId  = "pay_TESTPAY001";
        const string tamperedOrderId   = "order_MALICIOUS999";

        // Signature computed with the original (correct) order id
        var signature = ComputeExpectedSignature(gatewayOrderId, gatewayPaymentId, keySecret);
        var gateway   = BuildGateway(keySecret);

        // Caller submits a different order id — must be rejected
        var result = await gateway.VerifySignatureAsync(tamperedOrderId, gatewayPaymentId, signature);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifySignatureAsync_WithNullSignature_ReturnsFalse()
    {
        var gateway = BuildGateway("any_secret");

        var result = await gateway.VerifySignatureAsync("order_X", "pay_X", null!);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifySignatureAsync_WithEmptySignature_ReturnsFalse()
    {
        var gateway = BuildGateway("any_secret");

        var result = await gateway.VerifySignatureAsync("order_X", "pay_X", string.Empty);

        Assert.False(result);
    }
}
