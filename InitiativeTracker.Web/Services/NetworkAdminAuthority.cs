using System.Security.Cryptography;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using Microsoft.AspNetCore.DataProtection;

namespace InitiativeTracker.Web.Services;

/// <summary>Legacy DM cookies never grant N8 network authority. A fresh physical code is consumed over trusted HTTPS.</summary>
public sealed class NetworkAdminAuthority(IDataProtectionProvider provider, IProvisioningHttps tls, TimeProvider? clock = null)
{
    public const string CookieName = "__Host-InitiativeTracker.NetworkAdmin";
    private readonly IDataProtector protector = provider.CreateProtector("InitiativeTracker.NetworkAdmin.v1");
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly string processId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private string? code;
    private DateTimeOffset expires;
    private int failures;
    private sealed record Session(string ProcessId, DateTimeOffset Expires);
    public string PhysicalCode()
    {
        lock(gate)
        {
            if(code is null || time.GetUtcNow()>=expires || failures>=5)
            {code=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));expires=time.GetUtcNow().AddMinutes(5);failures=0;}
            return code;
        }
    }
    public bool TrustedHttps(HttpContext context) => context.Request.IsHttps && tls.Ready && context.Request.Host.Host == tls.Host;
    public string? Unlock(HttpContext context,string supplied)
    {
        if(!TrustedHttps(context)) return null;
        lock(gate)
        {
            if(code is null || failures>=5 || time.GetUtcNow()>=expires || supplied.Length!=32 ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(code),System.Text.Encoding.ASCII.GetBytes(supplied.ToUpperInvariant())))
            {failures++;return null;}
            code=null;
            return protector.Protect(JsonSerializer.Serialize(new Session(processId,time.GetUtcNow().AddMinutes(15))));
        }
    }
    public bool Allows(HttpContext context)
    {
        if(CoordinationEndpoints.Local(context))return true;
        if(!TrustedHttps(context)||!context.Request.Cookies.TryGetValue(CookieName,out var cookie))return false;
        try
        {
            var session=JsonSerializer.Deserialize<Session>(protector.Unprotect(cookie));
            return session is not null && session.ProcessId==processId && time.GetUtcNow()<session.Expires;
        }
        catch(Exception ex) when(ex is CryptographicException or JsonException){return false;}
    }
}
