using System.Net;
using System.Security.Cryptography.X509Certificates;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace InitiativeTracker.Tests;
public sealed class NetworkAdminAuthorityTests
{
    [Fact]
    public void LegacyHttpCookieCannotGrantNetworkAuthorityAndFreshPhysicalProofIsConsumedOverTrustedHttps()
    {
        var directory=Path.Combine(Path.GetTempPath(),"n8-admin-"+Guid.NewGuid());
        try
        {
            var clock=new Clock();var tls=new TestTls();var provider=DataProtectionProvider.Create(new DirectoryInfo(directory));
            var authority=new NetworkAdminAuthority(provider,tls,clock);var code=authority.PhysicalCode();
            var http=Context(false);http.Request.Headers.Cookie="InitiativeTracker.Auth=captured-gameplay-cookie";
            Assert.False(authority.Allows(http));Assert.Null(authority.Unlock(http,code));
            var https=Context(true);https.Request.Headers.Cookie=http.Request.Headers.Cookie;
            Assert.False(authority.Allows(https));
            var token=authority.Unlock(https,code);Assert.NotNull(token);Assert.Null(authority.Unlock(https,code));
            https.Request.Headers.Cookie=NetworkAdminAuthority.CookieName+"="+token;
            Assert.True(authority.Allows(https));
            http.Request.Headers.Cookie=https.Request.Headers.Cookie;Assert.False(authority.Allows(http));
            var restarted=new NetworkAdminAuthority(provider,tls,clock);Assert.False(restarted.Allows(https));
            clock.Advance(TimeSpan.FromMinutes(15));Assert.False(authority.Allows(https));
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]
    public void InvalidProofAttemptsAreBoundedAndPublicHttpsReadinessIsRechecked()
    {
        var directory=Path.Combine(Path.GetTempPath(),"n8-admin-"+Guid.NewGuid());
        try
        {
            var tls=new TestTls();var authority=new NetworkAdminAuthority(DataProtectionProvider.Create(new DirectoryInfo(directory)),tls);
            var code=authority.PhysicalCode();var context=Context(true);
            for(var i=0;i<5;i++)Assert.Null(authority.Unlock(context,new string('0',32)));
            Assert.Null(authority.Unlock(context,code));
            var fresh=authority.PhysicalCode();var token=authority.Unlock(context,fresh);Assert.NotNull(token);
            context.Request.Headers.Cookie=NetworkAdminAuthority.CookieName+"="+token;Assert.True(authority.Allows(context));
            tls.Ready=false;Assert.False(authority.Allows(context));
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private static DefaultHttpContext Context(bool https)
    {
        var result=new DefaultHttpContext();result.Request.Scheme=https?"https":"http";result.Request.Host=new HostString("network.synthetic.invalid");
        result.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.5");return result;
    }
    private sealed class TestTls:IProvisioningHttps
    {
        public bool Ready{get;set;}=true;public string Host=>"network.synthetic.invalid";public X509Certificate2? Certificate=>null;public X509Certificate2Collection Chain{get;}=[];
    }
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan span)=>now+=span;
    }
}
