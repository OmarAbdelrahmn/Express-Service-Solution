namespace Application.Contracts.DailyWork;

public record EditDailyWorkRequest(DateOnly Date, List<DailyWorkItem> Items);

public record DailyWorkItem(int? RiderId, long? EmployeeIqamaNo, int Orders, decimal WorkingHours);

public record DailyWorkResponseItem(int? RiderId, long EmployeeIqamaNo, int Orders, decimal WorkingHours);

public record DailyWorkResponse(DateOnly Date, List<DailyWorkResponseItem> Items);
