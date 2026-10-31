using System.Security.Cryptography;

namespace InitiativeTracker.Core.Coordination;

/// <summary>AP access and displayed codes are insufficient: independent owner proof is mandatory.</summary>
public sealed class OwnerSessionAuthority
{
    private readonly object gate = new();
    private readonly Dictionary<string,DateTimeOffset> sessions = [];
    private string? lease;
    private DateTimeOffset expiry;
    private int failures;
    private bool consumed;
    public string Code { get; private set; } = "";
    public DateTimeOffset ExpiresUtc { get { lock(gate) return expiry; } }
    public void OpenWindow(string leaseId, DateTimeOffset deadline)
    {
        lock(gate)
        {
            if (lease == leaseId) return;
            lease = leaseId; expiry = deadline; failures = 0; consumed = false; sessions.Clear();
            Code = RandomNumberGenerator.GetInt32(10000000,100000000).ToString();
        }
    }
    public string? Authorize(bool independentOwnerProof, bool headless, string? providedCode, DateTimeOffset now)
    {
        lock(gate)
        {
            if (failures >= 5 || consumed || now >= expiry || !independentOwnerProof || !headless && providedCode != Code)
            { failures++; return null; }
            consumed = true;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[token] = expiry < now.AddMinutes(10) ? expiry : now.AddMinutes(10);
            return token;
        }
    }
    public bool Valid(string? token, DateTimeOffset now)
    { lock(gate) return token is not null && sessions.TryGetValue(token,out var expires) && now < expires; }
    public void InvalidateSessions() { lock(gate) { sessions.Clear(); consumed = true; } }
}
