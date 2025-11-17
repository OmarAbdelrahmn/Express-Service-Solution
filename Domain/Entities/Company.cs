using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? FromTo { get; set; }
}
