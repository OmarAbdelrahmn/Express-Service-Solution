using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Application.Contracts.Employees;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VehicleDocumentType { Registration, Insurance, Inspection }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VehicleDocumentStatus { Missing, Expired, DueToday, Upcoming }

public sealed class VehicleDocumentQuery
{
    /// <summary>Omit to include registration, insurance and inspection.</summary>
    public VehicleDocumentType? DocumentType { get; set; }
    [Range(0, 3650)]
    public int DaysBeforeExpiry { get; set; } = 30;
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
    [Range(1, 200)]
    public int PageSize { get; set; } = 50;
}

public sealed record UpdateVehicleDocumentsRequest(
    DateOnly? LicenseExpiryDate,
    DateOnly? InsuranceExpiryDate,
    DateOnly? InspectionExpiryDate);

public sealed record VehicleDocumentReminder(
    string VehicleNumber,
    string PlateNumberA,
    string Location,
    VehicleDocumentType DocumentType,
    DateOnly? ExpiryDate,
    int? DaysUntilExpiry,
    VehicleDocumentStatus Status,
    string Message);

public sealed record VehicleDocumentReport(
    DateOnly CheckDate,
    int DaysBeforeExpiry,
    int Page,
    int PageSize,
    int TotalDocuments,
    int MissingCount,
    int ExpiredCount,
    int DueTodayCount,
    int UpcomingCount,
    IReadOnlyList<VehicleDocumentReminder> Items);
