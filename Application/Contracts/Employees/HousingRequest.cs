using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.Employees;

public record HousingRequest(
        string Name,
        string Address,
        int Capacity,
        long? ManagerIqamaNo
    );
public record HousingResponse(

    int Id,
    string Name,
    string Address,
    int Capacity,
    long? ManagerIqamaNo,
    ICollection<EmpolyeeResponse> Employees

    );
public record UHousingResponse(

    int Id,
    string Name,
    string Address,
    int Capacity,
    long? ManagerIqamaNo
    );

