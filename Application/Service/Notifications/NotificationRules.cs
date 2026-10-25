using Application.Abstraction;
using Application.Contracts.Notifications;
using Domain;
using Domain.Entities;
using Domain.Entities.Notifications;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Application.Service.Notifications;

public static class NotificationRules
{
    public static IQueryable<RiderDetails> EligibleRiders(ApplicationDbcontext db) => db.RiderDetails
        .Where(x => !x.Employee.IsDeleted && x.Employee.Status == "enable" && x.Employee.INKSA);
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static Error Invalid(string message) => new("Notifications.InvalidRequest", message, 400);
    public static readonly Error Forbidden = new("Notifications.Forbidden", "Notification permission is required.", 403);
    public static readonly Error NotFound = new("Notifications.NotFound", "Notification resource was not found.", 404);
    public static readonly Error Conflict = new("Notifications.Conflict", "The request conflicts with an existing registration or idempotency key. Retry registration with current session details.", 409);

    public static Error? Validate(SendNotificationRequest value, DateTime now)
    {
        if (value.Content is null || !value.Content.ContainsKey("en") || value.Content.Count > 3 ||
            value.Content.Any(x => x.Key is not ("ar" or "en" or "bn") || x.Value is null ||
                string.IsNullOrWhiteSpace(x.Value.Title) || x.Value.Title.Length > 120 ||
                string.IsNullOrWhiteSpace(x.Value.Body) || x.Value.Body.Length > 500))
            return Invalid("English content is required. Supported locales: ar, en, bn; title 1–120 and body 1–500 characters.");
        if (value.ExpiresInSeconds is < 60 or > 604800)
            return Invalid("expiresInSeconds must be between 60 and 604800.");
        if (value.Data is null || !value.Data.TryGetValue("type", out var type))
            return Invalid("An allowlisted action type is required.");
        if (type is "home" or "profile")
            return value.Data.Count == 1 ? null : Invalid("This action accepts only type.");
        var saudi = now.AddHours(3);
        if (type != "performance_report" || value.Data.Count != 3 ||
            !value.Data.TryGetValue("year", out var yearText) ||
            !value.Data.TryGetValue("month", out var monthText) ||
            !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            year < 2000 || month is < 1 or > 12 || year > saudi.Year || year == saudi.Year && month > saudi.Month)
            return Invalid("Invalid notification action or future report period.");
        return null;
    }

    public static (string Content, string Data, string Hash) Canonical(SendNotificationRequest value)
    {
        var content = JsonSerializer.Serialize(value.Content.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => new NotificationContent(x.Value.Title.Trim(), x.Value.Body.Trim())));
        var data = JsonSerializer.Serialize(value.Data.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary());
        return (content, data, Hash($"{content}|{data}|{value.ExpiresInSeconds}"));
    }

    public static NotificationRequest Create(SendNotificationRequest value, DateTime now, int upperId,
        int? riderId = null, int? companyId = null, string? actorId = null, string? scope = null, string? eventKey = null)
    {
        var canonical = Canonical(value);
        return new NotificationRequest
        {
            TargetRiderId = riderId, CompanyId = companyId, ActorId = actorId, IdempotencyScope = scope,
            EventKey = eventKey, PayloadHash = canonical.Hash, ContentJson = canonical.Content, DataJson = canonical.Data,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddSeconds(value.ExpiresInSeconds), UpperRiderId = upperId
        };
    }
}
