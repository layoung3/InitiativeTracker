using InitiativeTracker.Core.Coordination;
namespace InitiativeTracker.Tests;
public sealed class OwnerSessionTests
{
    [Fact]
    public void ScreenCodeAndApAccessAloneCannotAuthorizeAndLeaseCannotBeExtended()
    {
        var now = DateTimeOffset.UtcNow; var auth = new OwnerSessionAuthority(); auth.OpenWindow("lease1",now.AddMinutes(10));
        var code = auth.Code;
        Assert.Null(auth.Authorize(false,false,code,now));
        Assert.Null(auth.Authorize(true,false,"wrong",now));
        auth.OpenWindow("lease1",now.AddDays(1)); Assert.Equal(now.AddMinutes(10),auth.ExpiresUtc);
        Assert.Null(auth.Authorize(true,false,code,now.AddMinutes(11)));
    }
    [Fact]
    public void OwnerAndScreenCodeCreateOneTimeBoundedSession()
    {
        var now = DateTimeOffset.UtcNow; var auth = new OwnerSessionAuthority(); auth.OpenWindow("lease1",now.AddMinutes(10));
        var token = auth.Authorize(true,false,auth.Code,now);
        Assert.NotNull(token); Assert.True(auth.Valid(token,now)); Assert.False(auth.Valid(token,now.AddMinutes(10)));
        Assert.Null(auth.Authorize(true,false,auth.Code,now));
        auth.InvalidateSessions(); Assert.False(auth.Valid(token,now));
    }
    [Fact]
    public void HeadlessRequiresOwnerProofAndFailuresAreBoundedUntilNewPhysicalLease()
    {
        var now = DateTimeOffset.UtcNow; var auth = new OwnerSessionAuthority(); auth.OpenWindow("lease1",now.AddMinutes(10));
        for(var i=0;i<5;i++) Assert.Null(auth.Authorize(false,true,null,now));
        Assert.Null(auth.Authorize(true,true,null,now));
        auth.OpenWindow("lease2",now.AddMinutes(10)); Assert.NotNull(auth.Authorize(true,true,null,now));
    }
    [Fact]
    public void RestartDoesNotRestorePreviouslyAuthorizedPhoneSessions()
    {
        var now = DateTimeOffset.UtcNow; var auth = new OwnerSessionAuthority(); auth.OpenWindow("lease1",now.AddMinutes(10));
        var token = auth.Authorize(true,true,null,now);
        var restarted = new OwnerSessionAuthority(); restarted.OpenWindow("lease1",now.AddMinutes(10));
        Assert.False(restarted.Valid(token,now));
    }
}
