using Application.Abstraction;
using Application.Contracts.Employees;

namespace Application.Service.Empolyee;

public interface IHousingService
{
    Task<Result<IEnumerable<HousingResponse>>> GetAllEmployee();
    Task<Result<IEnumerable<HousingResponse>>> Get(string Name);
    Task<Result<IEnumerable<HousingResponse>>> GetWithManagerIqama(long ManagerIqamaNo);
    Task<Result<HousingResponse>> CreateAsync(HousingRequest Request);
    Task<Result<UHousingResponse>> UpdateAsync(string editHousingName, HousingRequest Request);
    Task<Result> DeleteAsync(string Name, CancellationToken cancellationToken = default);
    Task<Result> RemoveEmployeeFromHousing(long IqamaNo);
}
