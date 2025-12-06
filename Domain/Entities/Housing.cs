using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class Housing
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public ICollection<Employees> Employees { get; set; } = [];
    public long? ManagerIqamaNo { get; set; }
}
