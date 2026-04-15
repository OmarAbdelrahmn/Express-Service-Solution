using Application.Abstraction;
using Application.Contracts.Petrol;
using Domain.Entities.Petrol;
using Microsoft.AspNetCore.Http;

namespace Application.Service.Petrol;

public interface IPetrolService
{
    Task<Result<PetrolUploadResult>> ProcessUploadAsync(
        IFormFile file,
        DateOnly reportDate,
        string uploadedBy,
        CancellationToken ct = default);

    Task<Result> AttributePendingAsync(CancellationToken ct = default);

    Task<Result> AttributeSingleByIdAsync(int vehiclePetrolCostId, CancellationToken ct = default);

    Task<Result<RiderPetrolMonthlyReport>> GetRiderMonthlyReportAsync(
        long riderIqamaNo,
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<RiderPetrolSummaryRow>>> GetAllRidersSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<VehiclePetrolMonthlyReport>> GetVehicleMonthlyReportAsync(
        string vehicleNumber,
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<VehiclePetrolSummaryRow>>> GetAllVehiclesSummaryAsync(
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<VehicleUnattributedEntry>>> GetUnattributedCostsAsync(
        int year,
        int month,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<RiderDailyPetrolEntry>>> GetRiderCostsOnDateAsync(
        long riderIqamaNo,
        DateOnly date,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<VehicleDailyPetrolEntry>>> GetVehicleCostsOnDateAsync(
        string vehicleNumber,
        DateOnly date,
        CancellationToken ct = default);
}