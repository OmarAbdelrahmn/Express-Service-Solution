using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.Employees;

public record CompanyResponse
(
    int Id,
    string Name,
    string? Details,
    string? Address,
    string? Phone,
    string? Email
    );
public record CompanyRequest
(
    string Name,
    string? Details,
    string? Address,
    string? Phone,
    string? Email
    );

