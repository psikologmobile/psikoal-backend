using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PsikoAl.Api.Extensions;
using PsikoAl.Common.Dtos.Payment;
using PsikoAl.Common.Dtos.Payment.Create;
using PsikoAl.Services.Abstractions;

namespace PsikoAl.Api.Controllers;

// PayTR iFrame API. Sözleşme: docs/PAYMENTS.md.
//   1. adım: https://dev.paytr.com/iframe-api/iframe-api-1-adim
//   2. adım (Bildirim URL): https://dev.paytr.com/iframe-api/iframe-api-2-adim
[ApiController]
[Route("payments/paytr")]
public sealed class PayTrPaymentsController(IWalletPaymentService paymentService) : ControllerBase
{
    private const string PlainText = "text/plain";

    [Authorize]
    [HttpPost("init")]
    [EnableRateLimiting(RateLimitPolicies.PaymentInit)]
    public async Task<IActionResult> Init(PayTrInitRequestDto request, CancellationToken cancellationToken)
    {
        var result = await paymentService.InitPayTrAsync(
            this.CurrentUserId(),
            request.AmountKurus,
            ClientIp(HttpContext.Connection.RemoteIpAddress),
            cancellationToken);
        return Ok(new { data = result });
    }

    // PayTR sunucusu form-urlencoded POST eder ve yanıtın düz metin "OK" olmasını bekler.
    // "OK" dışındaki her yanıt bildirimi başarısız sayar ve PayTR tekrar dener; geçersiz
    // hash ya da bilinmeyen sipariş bu yüzden bilerek "OK" almaz (panelde görünür kalır).
    [AllowAnonymous]
    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.PaymentCallback)]
    public async Task<IActionResult> Callback([FromForm] IFormCollection form, CancellationToken cancellationToken)
    {
        var callback = ToCallback(form);
        if (callback is null)
        {
            return BadRequest("MISSING_FIELDS");
        }

        var outcome = await paymentService.HandlePayTrCallbackAsync(callback, cancellationToken);
        return outcome switch
        {
            PayTrCallbackOutcome.InvalidHash => BadRequest("INVALID_HASH"),
            PayTrCallbackOutcome.UnknownOrder => NotFound("UNKNOWN_ORDER"),
            _ => Content("OK", PlainText),
        };
    }

    private static PayTrCallbackDto? ToCallback(IFormCollection form)
    {
        string? Field(string name) => form.TryGetValue(name, out var value) ? value.ToString() : null;

        var merchantOid = Field("merchant_oid");
        var status = Field("status");
        var totalAmount = Field("total_amount");
        var hash = Field("hash");
        if (string.IsNullOrEmpty(merchantOid) || string.IsNullOrEmpty(status)
            || string.IsNullOrEmpty(totalAmount) || string.IsNullOrEmpty(hash))
        {
            return null;
        }

        return new PayTrCallbackDto(
            merchantOid,
            status,
            totalAmount,
            hash,
            Field("failed_reason_code"),
            Field("failed_reason_msg"),
            Field("test_mode"),
            Field("payment_type"),
            Field("currency"),
            Field("payment_amount"));
    }

    // RemoteIpAddress, UseForwardedHeaders sonrası istemcinin gerçek adresidir
    // (ForwardedHeadersSetup). IPv4-mapped IPv6 adres PayTR'ye IPv4 olarak gider.
    private static string ClientIp(IPAddress? address)
    {
        if (address is null)
        {
            return IPAddress.Loopback.ToString();
        }

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }
}
