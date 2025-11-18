using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderCompanyHistory
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public int CompanyId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }  
    public string Reason { get; set; } = string.Empty;
}
