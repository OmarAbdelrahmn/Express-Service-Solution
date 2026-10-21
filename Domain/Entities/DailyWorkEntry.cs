namespace Domain.Entities;

public class DailyWorkEntry
{
    public DateOnly Date { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public int Orders { get; set; }
    public decimal WorkingHours { get; set; }
    public Employees Employee { get; set; } = default!;
}
