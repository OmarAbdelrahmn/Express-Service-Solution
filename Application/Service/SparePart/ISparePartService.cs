using Application.Abstraction;
using Application.Contracts.SparePartCo;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.SparePart;

public interface ISparePartService
{
    Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync();
    Task<Result<SparePartResponse>> GetByIdAsync(int id);
    Task<Result<SparePartResponse>> CreateAsync(SparePartRequest request);
    Task<Result<SparePartResponse>> UpdateAsync(int id, SparePartRequest request);
    Task<Result> DeleteAsync(int id);
    Task<Result<IEnumerable<SparePartResponse>>> SearchAsync(string keyword);
    Task<Result<SparePartResponse>> RecordUsageAsync(int sparePartId, SparePartUsageRequest request);
    Task<Result<IEnumerable<SparePartUsageResponse>>> GetUsageHistoryAsync(int sparePartId);
    Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleUsageHistoryAsync(string vehicleNumber);
}