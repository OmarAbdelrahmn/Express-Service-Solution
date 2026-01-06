using Application.Abstraction;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Member;
public static class HousingMemberErrors
{
    public static readonly Error NotAHousingManager = new(
    "HousingMember.NotAManager",
    "You are not assigned as a housing manager",
    StatusCodes.Status403Forbidden
);

    public static readonly Error HousingNotFound = new(
        "HousingMember.HousingNotFound",
        "Housing not found",
        StatusCodes.Status404NotFound
    );

    public static readonly Error EmployeeNotInHousing = new(
        "HousingMember.EmployeeNotInHousing",
        "This employee is not in your housing",
        StatusCodes.Status403Forbidden
    );

    public static readonly Error RiderNotInHousing = new(
        "HousingMember.RiderNotInHousing",
        "This rider is not in your housing",
        StatusCodes.Status403Forbidden
    );

    public static readonly Error VehicleNotInHousing = new(
        "HousingMember.VehicleNotInHousing",
        "This vehicle is not assigned to your housing",
        StatusCodes.Status403Forbidden
    );

    public static readonly Error InvalidIqamaNumber = new(
        "HousingMember.InvalidIqamaNumber",
        "Invalid Iqama number format",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error MemberMustLoginWithIqama = new(
        "HousingMember.MustLoginWithIqama",
        "Housing members must login using their Iqama number",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error RequestNotFound = new(
    "HousingMember.RequestNotFound",
    "Request not found",
    StatusCodes.Status404NotFound
);

    public static readonly Error RequestAlreadyResolved = new(
        "HousingMember.RequestAlreadyResolved",
        "This request has already been resolved",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error UnauthorizedToCancel = new(
        "HousingMember.UnauthorizedToCancel",
        "You are not authorized to cancel this request",
        StatusCodes.Status403Forbidden
    );

    public static readonly Error InvalidRequestType = new(
        "HousingMember.InvalidRequestType",
        "Invalid request type",
        StatusCodes.Status400BadRequest
    );
}
