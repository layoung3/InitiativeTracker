using System.Security.Cryptography;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public class DeviceRegistryService
{
    private readonly object _lock = new();

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    public DeviceRegistryService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public DevicePairingRequest CreatePairingRequest()
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var now =
                DateTime.UtcNow;

            var code =
                GenerateUniquePairingCode(db);

            var request =
                new DevicePairingRequest
                {
                    PairingCode = code,

                    DeviceName =
                        $"Display-{code}",

                    Status =
                        "Pending",

                    RequestedAtUtc =
                        now,

                    ExpiresAtUtc =
                        now.AddMinutes(10)
                };

            db.DevicePairingRequests.Add(
                request);

            db.SaveChanges();

            return request;
        }
    }

    public DevicePairingRequest?
        GetPairingRequest(Guid requestId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.DevicePairingRequests
            .AsNoTracking()
            .SingleOrDefault(x => x.Id == requestId);
    }

    public IReadOnlyList<DevicePairingRequest>
        GetPendingRequests()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var now =
            DateTime.UtcNow;

        return db.DevicePairingRequests
            .AsNoTracking()
            .Where(x =>
                x.Status == "Pending" &&
                x.ExpiresAtUtc > now)
            .OrderBy(x => x.RequestedAtUtc)
            .ToList();
    }

    public IReadOnlyList<RegisteredDevice>
        GetTrustedDisplays()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.RegisteredDevices
            .AsNoTracking()
            .Where(x =>
                x.IsTrusted &&
                x.DeviceType == "Display")
            .OrderBy(x => x.Name)
            .ToList();
    }

    public RegisteredDevice ApprovePairing(
        Guid requestId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var request =
                db.DevicePairingRequests
                    .SingleOrDefault(
                        x => x.Id == requestId)
                ?? throw new ArgumentException(
                    "Pairing request was not found.",
                    nameof(requestId));

            if (request.Status != "Pending")
            {
                throw new InvalidOperationException(
                    "Pairing request is no longer pending.");
            }

            if (request.ExpiresAtUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException(
                    "Pairing request has expired.");
            }

            var device =
                new RegisteredDevice
                {
                    Name =
                        request.DeviceName,

                    DeviceType =
                        "Display",

                    IsTrusted =
                        true,

                    PairedAtUtc =
                        DateTime.UtcNow
                };

            db.RegisteredDevices.Add(
                device);

            request.Status =
                "Approved";

            request.ApprovedDeviceId =
                device.Id;

            db.SaveChanges();

            return device;
        }
    }

    public void RejectPairing(
        Guid requestId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var request =
                db.DevicePairingRequests
                    .SingleOrDefault(
                        x => x.Id == requestId)
                ?? throw new ArgumentException(
                    "Pairing request was not found.",
                    nameof(requestId));

            request.Status =
                "Rejected";

            db.SaveChanges();
        }
    }

    public void MarkPairingClaimed(
        Guid requestId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var request =
                db.DevicePairingRequests
                    .SingleOrDefault(
                        x => x.Id == requestId);

            if (request is null)
            {
                return;
            }

            request.Status =
                "Claimed";

            db.SaveChanges();
        }
    }

    public bool IsTrustedDisplay(
        Guid deviceId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.RegisteredDevices
            .AsNoTracking()
            .Any(x =>
                x.Id == deviceId &&
                x.IsTrusted &&
                x.DeviceType == "Display");
    }

    public void MarkDeviceSeen(
        Guid deviceId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var device =
                db.RegisteredDevices
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            deviceId &&
                            x.IsTrusted);

            if (device is null)
            {
                return;
            }

            device.LastSeenAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }


    public void ForgetDevice(
        Guid deviceId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var device =
                db.RegisteredDevices
                    .SingleOrDefault(
                        x => x.Id == deviceId);

            if (device is null)
            {
                return;
            }

            device.IsTrusted =
                false;

            db.SaveChanges();
        }
    }

    public void RenameDevice(
        Guid deviceId,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Device name is required.",
                nameof(name));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var device =
                db.RegisteredDevices
                    .SingleOrDefault(
                        x => x.Id == deviceId)
                ?? throw new ArgumentException(
                    "Device was not found.",
                    nameof(deviceId));

            device.Name =
                name.Trim();

            db.SaveChanges();
        }
    }

    private static string GenerateUniquePairingCode(
        InitiativeTrackerDbContext db)
    {
        for (var attempt = 0;
             attempt < 20;
             attempt++)
        {
            var code =
                RandomNumberGenerator
                    .GetInt32(0, 10000)
                    .ToString("D4");

            var exists =
                db.DevicePairingRequests.Any(
                    x =>
                        x.PairingCode == code &&
                        x.Status == "Pending" &&
                        x.ExpiresAtUtc >
                            DateTime.UtcNow);

            if (!exists)
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "Unable to generate a pairing code.");
    }
}