using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using InitiativeTracker.Satellite;
using InitiativeTracker.Core.Coordination;
using QRCoder;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SatelliteAgent>();
builder.Services.AddHostedService<SatelliteWorker>();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var phoneCertificate = builder.Configuration["Satellite:PhoneCertificate"];
var phoneName = builder.Configuration["Satellite:PhoneHost"];
IProvisioningHttps phoneTls = new PublicCertificateHttps(phoneName, phoneCertificate, builder.Configuration["Satellite:PhoneCertificatePassword"]);
builder.Services.AddSingleton(phoneTls);
var phoneEnabled = phoneTls.Ready;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5081);
    // A phone-trusted certificate is supplied only by the approval-gated installation workflow.
    if (phoneEnabled) options.ListenAnyIP(5444, listen => listen.UseHttps(tls => { tls.ServerCertificate = phoneTls.Certificate; tls.ServerCertificateChain = phoneTls.Chain; }));
});
var app = builder.Build();
var agent = app.Services.GetRequiredService<SatelliteAgent>();
var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var authority = new OwnerSessionAuthority();
void RefreshLease(System.Text.Json.JsonElement lease)
{
    if (lease.ValueKind == System.Text.Json.JsonValueKind.Null) return;
    authority.OpenWindow(lease.GetProperty("created").GetRawText(),
        DateTimeOffset.FromUnixTimeSeconds((long)lease.GetProperty("deadline").GetDouble()));
}
string E(string? text) => HtmlEncoder.Default.Encode(text ?? "");
bool Local(HttpContext c) => c.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip) && c.Request.Host.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1";
bool Session(HttpContext c)
{
    if (Local(c)) return true;
    return c.Request.Cookies.TryGetValue("IT.Setup", out var cookie) && authority.Valid(cookie,DateTimeOffset.UtcNow);
}
string Form(string action, string fields, string label) => $"<form method='post' action='/{action}'><input type='hidden' name='csrf' value='{csrf}'>{fields}<button>{E(label)}</button></form>";
string Input(string name, string label, string type = "text") => $"<label>{E(label)}<input name='{name}' type='{type}' autocomplete='off' required></label>";
string Page(string body) => "<!doctype html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'><title>Satellite Setup</title><style>body{font:22px system-ui;max-width:950px;margin:25px auto;padding:20px}input,button{font:inherit;padding:12px;margin:8px;max-width:90%}label{display:block}code{overflow-wrap:anywhere}button{cursor:pointer}.keys button{padding:8px;margin:2px}</style></head><body>" + body + "<div class='keys' id='keys'></div><script>let field;document.querySelectorAll('input:not([type=hidden])').forEach(e=>e.onfocus=()=>field=e);for(const c of 'abcdefghijklmnopqrstuvwxyz0123456789.-:/ ABCDEFGHIJKLMNOPQRSTUVWXYZ@!#$%&*()_+=?'){const b=document.createElement('button');b.type='button';b.textContent=c;b.onclick=()=>{if(field)field.value+=c};document.getElementById('keys').append(b)}const back=document.createElement('button');back.textContent='⌫';back.onclick=()=>{if(field)field.value=field.value.slice(0,-1)};document.getElementById('keys').append(back);</script></body></html>";
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; frame-ancestors 'none'; form-action 'self'";
    if (!Local(context))
    {
        if (!phoneTls.Ready || !context.Request.IsHttps || context.Request.Host.Host != phoneName)
        { context.Response.StatusCode = 403; return; }
        var status = await agent.Broker(new { action = "provisioning-info" }, context.RequestAborted);
        RefreshLease(status.GetProperty("lease"));
        if (status.GetProperty("lease").ValueKind == System.Text.Json.JsonValueKind.Null)
        { context.Response.StatusCode = 403; return; }
    }
    if (context.Request.Method == "POST")
    {
        var expected = context.Request.Scheme + "://" + context.Request.Host;
        if (context.Request.Headers.Origin.ToString() != expected || context.Request.ContentLength is > 8192 || !context.Request.HasFormContentType)
        { context.Response.StatusCode = 403; return; }
        if (context.Request.Path is { } secretPath && (secretPath == "/network" || secretPath == "/authorize" || secretPath == "/reassign") && (!context.Request.IsHttps || !phoneTls.Ready))
        { context.Response.StatusCode = 403; return; }
        var form = await context.Request.ReadFormAsync();
        if (form["csrf"] != csrf || context.Request.Path != "/authorize" && !Session(context))
        { context.Response.StatusCode = 403; return; }
    }
    await next();
});
app.MapGet("/", async (HttpContext context, CancellationToken ct) =>
{
    if (!Session(context)) return Results.Content(Page("<h1>Owner authorization</h1>" + Form("authorize", Input("owner", "Owner recovery credential", "password") +
        (builder.Configuration.GetValue<bool>("Satellite:Headless") ? "" : Input("code", "One-time code on satellite screen")), "Authorize setup")), "text/html");
    var access = await agent.Broker(new { action = "provisioning-info" }, ct);
    RefreshLease(access.GetProperty("lease"));
    var body = $"<h1>Satellite Setup</h1><p>{E(agent.Message)}</p><p>Device: {agent.Snapshot.DeviceId}</p><p>Physical satellite certificate SHA-256: <code>{agent.Pin}</code></p>";
    if (Local(context))
    {
        body += $"<p>One-time phone authorization code: <strong>{authority.Code}</strong> (expires {authority.ExpiresUtc:HH:mm} UTC). The separate owner credential is also required.</p>";
        if (agent.SetupOwnerCredential is { } owner) body += $"<p>Save this unique owner recovery credential securely before pairing: <code>{owner}</code>. It will no longer be displayed after setup.</p>";
        body += Form("setup", "<p>Opening setup Wi-Fi pauses hub connectivity on this single-radio device for at most ten minutes. It preserves pairing.</p>", "Open temporary phone setup Wi-Fi");
    }
    if (Local(context) && access.GetProperty("access").ValueKind != System.Text.Json.JsonValueKind.Null)
    {
        var ap = access.GetProperty("access");
        string Escape(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(":", "\\:").Replace(",", "\\,").Replace("\"", "\\\"");
        var payload = "WIFI:T:WPA;S:" + Escape(ap.GetProperty("ssid").GetString()!) + ";P:" + Escape(ap.GetProperty("password").GetString()!) + ";;";
        using var qr = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        body += "<p>Private setup Wi-Fi access QR. This grants network access only; owner authorization is still required.</p><img alt='Join private setup Wi-Fi' width='250' src='data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(qr).GetGraphic(6)) + "'>";
        body += phoneEnabled ? $"<p>After joining, open <a href='https://{E(phoneName)}:5444/'>https://{E(phoneName)}:5444/</a>. Use the configured publicly trusted provisioning endpoint. Certificate validation must succeed; there is no certificate-warning bypass.</p>" : "<p>Phone HTTPS is not configured. Use the local touchscreen; phone setup stays closed until approved trust configuration is installed.</p>";
    }
    if (context.Request.IsHttps && phoneTls.Ready) body += Form("network", Input("ssid", "Destination Wi-Fi SSID") + Input("password", "Wi-Fi password", "password") +
        "<p>The phone disconnects while the satellite joins this network. Setup returns to its previous network when the fixed lease expires.</p>", "Join destination network temporarily");
    body += Form("pair", Input("url", "Destination hub HTTPS origin") + Input("pin", "Hub SHA-256 from physical hub screen") + Input("name", "Satellite name"), "Authorize pairing or reassignment with this hub");
    if (context.Request.IsHttps && phoneTls.Ready) body += Form("reassign", Input("url", "Destination hub HTTPS origin") + Input("pin", "Hub SHA-256 from physical hub screen") + Input("name", "Satellite name") + Input("ssid", "Destination Wi-Fi SSID") + Input("password", "Wi-Fi password", "password") + "<p>This explicitly authorizes replacement only after the specified hub's DM approves. Your phone disconnects while the satellite joins the destination; it finishes automatically after approval, or returns independently when setup expires.</p>", "Authorize destination and request reassignment");
    if (!phoneTls.Ready) body += "<p>Password submission is blocked. Publicly trusted HTTPS provisioning is not configured. Existing saved-network recovery and agent coordination remain available.</p>";
    if (agent.Snapshot.Candidate is not null) body += Form("finish", "<p>Finish only after the new DM approves the full satellite fingerprint. This explicitly replaces the previous association and revokes its local authority.</p>", "Authorize and finish reassignment");
    body += Form("forget", "<p>Explicitly remove the old association and its saved agent profiles. This pauses display operation and requires owner authorization.</p>", "Authorize forgetting old hub");
    body += Form("cancel", "", "Cancel setup and restore previous display");
    if (agent.Snapshot.Binding is { } binding) body += $"<p><a href='{E(binding.DisplayUrl)}'>Return to paired hub display</a></p>";
    return Results.Content(Page(body), "text/html");
});
app.MapPost("/authorize", async (HttpContext c) =>
{
    var f = await c.Request.ReadFormAsync();
    var session = authority.Authorize(agent.Owner(f["owner"]), builder.Configuration.GetValue<bool>("Satellite:Headless"), f["code"], DateTimeOffset.UtcNow);
    if (session is null) return Results.StatusCode(403);
    c.Response.Cookies.Append("IT.Setup", session, new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = authority.ExpiresUtc - DateTimeOffset.UtcNow });
    return Results.Redirect("/");
});
app.MapPost("/{action}", async (string action, HttpContext c, CancellationToken ct) =>
{
    var f = await c.Request.ReadFormAsync();
    try
    {
        switch (action)
        {
            case "setup":
                if (!Local(c)) return Results.StatusCode(403);
                await agent.OpenSetup(ct);
                RefreshLease((await agent.Broker(new { action = "provisioning-info" },ct)).GetProperty("lease"));
                break;
            case "reassign": await agent.BeginReassignment(f["url"].ToString(), f["pin"].ToString(), f["name"].ToString(), f["ssid"].ToString(), f["password"].ToString(), ct); break;
            case "network": await agent.ConfigureNetwork(f["ssid"].ToString(), f["password"].ToString(), ct); break;
            case "pair": await agent.RequestPairing(f["url"].ToString(), f["pin"].ToString(), f["name"].ToString(), ct); break;
            case "finish": await agent.FinishPairing(ct); authority.InvalidateSessions(); break;
            case "forget": await agent.Forget(ct); authority.InvalidateSessions(); break;
            case "cancel": await agent.CancelCandidate(ct); authority.InvalidateSessions(); break;
            default: return Results.NotFound();
        }
        return Results.Redirect("/");
    }
    catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or System.Net.Sockets.SocketException)
    { return Results.Content(Page("<h1>Setup deferred</h1><p>Network operation failed or destination approval is missing. Existing pairing is retained. Check the physical hub and retry within the current setup window.</p><a href='/'>Return to setup</a>"), "text/html", statusCode: 409); }
});
app.Run();
