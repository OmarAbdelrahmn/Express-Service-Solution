using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class Employees
{
    public long IqamaNo { get; set; }
    public DateOnly IqamaEndM { get; set; }
    public DateOnly IqamaEndH { get; set; }
    public string? PassportNo { get; set; } = string.Empty;
    public DateOnly? PassportEnd { get; set; }
    public string Sponsor { get; set; } = string.Empty;
    public long sponsorNo { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string NameAR { get; set; } = string.Empty;
    public string NameEN { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Status { get; set; } = "enable";
    public string? IBAN { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public bool INKSA { get; set; } = true;


    public int? HousingId { get; set; }
    public Housing? Housing { get; set; } 

    public RiderDetails? RiderDetails { get; set; }

    public EmployeeDocuments? EmployeeDocuments { get; set; }

    }

