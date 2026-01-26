using Application.Abstraction;
using Application.Contracts.RiderAccessoryCon;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.RiderAccessory;

public interface IRiderAccessoryService
{
    Task<Result<IEnumerable<RiderAccessoryResponse>>> GetAllAsync();
    Task<Result<RiderAccessoryResponse>> GetByIdAsync(int id);
    Task<Result<RiderAccessoryResponse>> CreateAsync(RiderAccessoryRequest request);
    Task<Result<RiderAccessoryResponse>> UpdateAsync(int id, RiderAccessoryRequest request);
    Task<Result> DeleteAsync(int id);
    Task<Result<IEnumerable<RiderAccessoryResponse>>> SearchAsync(string keyword);
    Task<Result<RiderAccessoryUsageResponse>> IssueToRiderAsync(int accessoryId, IssueAccessoryRequest request);
    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetRiderAccessoriesAsync(int riderId);
    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetAccessoryHistoryAsync(int accessoryId);
}