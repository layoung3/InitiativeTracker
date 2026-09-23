namespace InitiativeTracker.Web.Services;

public class TrackerUrlService
{
    private readonly IConfiguration
        _configuration;

    public TrackerUrlService(
        IConfiguration configuration)
    {
        _configuration =
            configuration;
    }

    public string GetBaseUrl(
        HttpRequest request)
    {
        var configured =
            _configuration[
                "Tracker:PublicBaseUrl"];

        if (!string.IsNullOrWhiteSpace(
                configured))
        {
            return configured
                .Trim()
                .TrimEnd('/');
        }

        return
            $"{request.Scheme}://{request.Host}";
    }

    public string GetPlayerJoinUrl(
        HttpRequest request,
        string token)
    {
        var baseUrl =
            GetBaseUrl(request);

        return
            $"{baseUrl}/join?t=" +
            Uri.EscapeDataString(token);
    }
}