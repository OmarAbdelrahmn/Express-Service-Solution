using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; } 
    public string? Email { get; set; }
}
