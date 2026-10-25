using Application.Abstraction;
using Application.Authentication;
using Application.Contracts.RiderMobile;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using Application.Service.Notifications;

namespace Application.Service.RiderMobile;

public partial class RiderMobileService(ApplicationDbcontext db, IRiderMobileTokenProvider tokens,
    IOptions<RiderMobileOptions> options, TimeProvider clock) : IRiderMobileService
{
    private readonly RiderMobileOptions settings = options.Value;
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    // Shift dates in this system are Saudi calendar dates; session instants are UTC.
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)).DateTime);
    private IQueryable<RiderDetails> ActiveRiders => db.RiderDetails.Where(x =>
        !x.Employee.IsDeleted && x.Employee.Status == "enable" && x.Employee.INKSA);

    private static Error Error(string code, string language, int status) => new(
        "RiderMobile." + code, RiderMobileText.Get(language, code), status);

    public async Task<Result<RiderMobileLoginResponse>> LoginAsync(RiderMobileLoginRequest request, CancellationToken ct = default)
    {
        var language = request.Language?.Trim().ToLowerInvariant();
        if (language is not null && !RiderMobileText.IsLanguage(language))
            return Result.Failure<RiderMobileLoginResponse>(Error("InvalidLanguage", "en", 400));
        if (request.IqamaNo is null || request.IqamaNo.Length != 10 || request.IqamaNo.Any(c => c is < '0' or > '9') ||
            !long.TryParse(request.IqamaNo, NumberStyles.None, CultureInfo.InvariantCulture, out var iqama))
            return Result.Failure<RiderMobileLoginResponse>(Error("InvalidLogin", language ?? "ar", 401));

        var rider = await ActiveRiders.Include(x => x.Employee).Include(x => x.Company).Include(x => x.Vehicle)
            .SingleOrDefaultAsync(x => x.EmployeeIqamaNo == iqama, ct);
        if (rider is null)
            return Result.Failure<RiderMobileLoginResponse>(Error("InvalidLogin", language ?? "ar", 401));

        language ??= RiderMobileText.IsLanguage(rider.PreferredLanguage) ? rider.PreferredLanguage : "ar";
        rider.PreferredLanguage = language;
        var now = UtcNow;
        var session = new RiderMobileSession
        {
            RiderId = rider.Id, Rider = rider, Language = language,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(settings.SessionDays)
        };
        db.RiderMobileSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return Result.Success(await LoginResponseAsync(session, rider, ct));
    }

    public Task<bool> IsSessionValidAsync(Guid sessionId, int riderId, string language, CancellationToken ct = default)
    {
        var now = UtcNow;
        return db.RiderMobileSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId && x.RiderId == riderId &&
            x.Language == language && x.RevokedAtUtc == null && x.ExpiresAtUtc > now &&
            !x.Rider.Employee.IsDeleted && x.Rider.Employee.Status == "enable" && x.Rider.Employee.INKSA, ct);
    }

    public async Task<Result<RiderMobileLoginResponse>> SetLanguageAsync(Guid sessionId, int riderId, string language, CancellationToken ct = default)
    {
        language = language?.Trim().ToLowerInvariant() ?? "";
        if (!RiderMobileText.IsLanguage(language))
            return Result.Failure<RiderMobileLoginResponse>(Error("InvalidLanguage", "en", 400));
        await using var transaction = await NotificationDeviceLock.BeginAsync(db, ct);
        var now = UtcNow;
        var session = await db.RiderMobileSessions.Include(x => x.Rider.Employee).Include(x => x.Rider.Company)
            .Include(x => x.Rider.Vehicle)
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.RiderId == riderId &&
                x.RevokedAtUtc == null && x.ExpiresAtUtc > now && !x.Rider.Employee.IsDeleted &&
                x.Rider.Employee.Status == "enable" && x.Rider.Employee.INKSA, ct);
        if (session is null)
            return Result.Failure<RiderMobileLoginResponse>(Error("InvalidSession", language, 401));

        session.RevokedAtUtc = now;
        session.Rider.PreferredLanguage = language;
        var replacement = new RiderMobileSession
        {
            RiderId = riderId, Rider = session.Rider, Language = language,
            CreatedAtUtc = now, ExpiresAtUtc = session.ExpiresAtUtc
        };
        db.RiderMobileSessions.Add(replacement);
        var devices = await db.RiderPushDevices.Where(x => x.SessionId == sessionId && x.RiderId == riderId && x.IsActive).ToListAsync(ct);
        foreach (var device in devices)
        {
            device.SessionId = replacement.Id;
            device.Generation++;
            device.UpdatedAtUtc = now;
        }
        if (session.Language != language)
            db.NotificationRequests.Add(NotificationRules.Create(NotificationTemplates.ProfileChanged, now, riderId,
                riderId: riderId, eventKey: $"profile-language:{replacement.Id:D}"));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Result.Success(await LoginResponseAsync(replacement, session.Rider, ct));
    }

    public async Task<Result> LogoutAsync(Guid sessionId, int riderId, CancellationToken ct = default)
    {
        await using var transaction = await NotificationDeviceLock.BeginAsync(db, ct);
        var session = await db.RiderMobileSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.RiderId == riderId, ct);
        if (session is not null && session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = UtcNow;
        }
        var devices = await db.RiderPushDevices.Where(x => x.SessionId == sessionId && x.RiderId == riderId && x.IsActive).ToListAsync(ct);
        foreach (var device in devices) { device.IsActive = false; device.Generation++; device.UpdatedAtUtc = UtcNow; }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result<RiderMobileProfile>> GetProfileAsync(int riderId, string language, CancellationToken ct = default)
    {
        var rider = await FindRiderAsync(riderId, ct);
        return rider is null
            ? Result.Failure<RiderMobileProfile>(Error("InvalidSession", language, 401))
            : Result.Success(await ProfileAsync(rider, language, Today, ct));
    }

    private Task<RiderDetails?> FindRiderAsync(int riderId, CancellationToken ct) => ActiveRiders.AsNoTracking()
        .Include(x => x.Employee).Include(x => x.Company).Include(x => x.Vehicle)
        .SingleOrDefaultAsync(x => x.Id == riderId, ct);

    private async Task<RiderMobileLoginResponse> LoginResponseAsync(RiderMobileSession session, RiderDetails rider, CancellationToken ct) =>
        new(tokens.Generate(session), "Bearer", session.ExpiresAtUtc, session.Language,
            await ProfileAsync(rider, session.Language, Today, ct));

    private async Task<RiderMobileProfile> ProfileAsync(RiderDetails rider, string language, DateOnly today, CancellationToken ct)
    {
        var start = new DateOnly(today.Year, today.Month, 1);
        var petrolCost = await db.RiderPetrolCosts.AsNoTracking()
            .Where(x => x.RiderIqamaNo == rider.EmployeeIqamaNo && x.Date >= start && x.Date <= today)
            .SumAsync(x => (decimal?)x.Cost, ct) ?? 0m;

        return new RiderMobileProfile(
            rider.Id, rider.EmployeeIqamaNo.ToString(CultureInfo.InvariantCulture), rider.WorkingId,
            language == "ar" ? rider.Employee.NameAR : rider.Employee.NameEN,
            rider.Employee.NameAR, rider.Employee.NameEN, rider.Company.Name, rider.Vehicle?.PlateNumberE, language,
            petrolCost);
    }
}
