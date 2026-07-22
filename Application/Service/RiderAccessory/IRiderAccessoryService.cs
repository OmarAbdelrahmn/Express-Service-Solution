using Application.Abstraction;
using Application.Contracts.RiderAccessoryCon;
using Application.Contracts.SparePartCo;

namespace Application.Service.RiderAccessory;

public interface IRiderAccessoryService
{
    Task<Result<IEnumerable<RiderAccessoryResponse>>> GetAllAsync();
    Task<Result<IEnumerable<RiderAccessoryResponse>>> GetAllAsync2();
    Task<Result<RiderAccessoryResponse>> GetByIdAsync(int id);
    Task<Result<RiderAccessoryResponse>> CreateAsync(RiderAccessoryRequest request, string performedBy);
    Task<Result<RiderAccessoryResponse>> UpdateAsync(int id, RiderAccessoryRequest request, string performedBy);
    Task<Result> DeleteAsync(int id, string performedBy);
    Task<Result<IEnumerable<RiderAccessoryResponse>>> SearchAsync(string keyword);
    Task<Result<RiderAccessoryUsageResponse>> IssueToRiderAsync(int accessoryId, IssueAccessoryRequest request);
    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetRiderAccessoriesAsync(int riderId);
    Task<Result<IEnumerable<RiderAccessoryUsageResponse>>> GetAccessoryHistoryAsync(int accessoryId);

    Task<Result<BatchUsageResponse>> RecordBatchRiderAccessoryUsageAsync(DateTime Date, BatchRiderAccessoryUsageRequest request);

}