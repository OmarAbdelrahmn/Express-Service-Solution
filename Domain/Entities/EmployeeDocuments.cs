using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain.Entities;

public class EmployeeDocuments
{

    public int Id { get; set; }
    [ForeignKey("EmployeeIqamaNo")]
    public int EmployeeIqamaNo { get; set; }  

   
    public string? ProfileImagePath { get; set; }  
    public string? PassportImagePath { get; set; }
    public string? IqamaImagePath { get; set; }
    public string? LicenseImagePath { get; set; }
    public string? WorkPermitImagePath { get; set; }
    public string? AdditionImage{ get; set; }
    public string? AdditionImage1 { get; set; }
    public string? AdditionImage2 { get; set; }
    public string? AdditionImage3 { get; set; }

    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Employees Employee { get; set; } = default!;
}
