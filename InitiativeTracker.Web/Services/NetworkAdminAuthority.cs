using System.Security.Cryptography;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using Microsoft.AspNetCore.DataProtection;

namespace InitiativeTracker.Web.Services;

/// <summary>Legacy DM cookies never grant N8 network authority. A fresh physical code is consumed over trusted HTTPS.</summary>
public sealed class NetworkAdminAuthority(IDataProtectionProvider provider, DmAuthenticationPolicy authentication, TimeProvider? clock = null)
{
    public const string CookieName = "__Host-InitiativeTracker.NetworkAdmin";
    private readonly IDataProtector protector = provider.CreateProtector("InitiativeTracker.NetworkAdmin.v2");
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly string processId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly object gate = new();
    private string? code;
    private DateTimeOffset expires;
    private int failures;
    private sealed record Session(string ProcessId, string LoginId, string GameSessionId, DateTimeOffset Expires);
    public string PhysicalCode()
    {
        lock(gate)
        {
            if(code is null || time.GetUtcNow()>=expires || failures>=5)
            {code=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));expires=time.GetUtcNow().AddMinutes(5);failures=0;}
            return code;
        }
    }
    public bool TrustedHttps(HttpContext context) => authentication.TrustedHttps(context);
    public bool PhysicalScreen(HttpContext context) => authentication.PhysicalScreen(context);
    public string? Unlock(HttpContext context,string supplied)
    {
        if(!authentication.SecureDm(context)) return null;
        lock(gate)
        {
            if(code is null || failures>=5 || time.GetUtcNow()>=expires || supplied.Length!=32 ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(code),System.Text.Encoding.ASCII.GetBytes(supplied.ToUpperInvariant())))
            {failures++;return null;}
            code=null;
            return protector.Protect(JsonSerializer.Serialize(new Session(processId,
                context.User.FindFirst(DmAuthenticationPolicy.LoginIdClaim)!.Value,
                context.User.FindFirst("session_id")!.Value,time.GetUtcNow().AddMinutes(15))));
        }
    }
    public bool Allows(HttpContext context)
    {
        if(!authentication.CoordinationEnabled && CoordinationEndpoints.Local(context))return true;
        if(!authentication.SecureDm(context)||!context.Request.Cookies.TryGetValue(CookieName,out var cookie))return false;
        try
        {
            var session=JsonSerializer.Deserialize<Session>(protector.Unprotect(cookie));
            return session is not null && session.ProcessId==processId && time.GetUtcNow()<session.Expires &&
                session.LoginId==context.User.FindFirst(DmAuthenticationPolicy.LoginIdClaim)?.Value &&
                session.GameSessionId==context.User.FindFirst("session_id")?.Value;
        }
        catch(Exception ex) when(ex is CryptographicException or JsonException){return false;}
    }
}
