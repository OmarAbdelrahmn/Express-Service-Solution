using Application.Abstraction;
using Microsoft.AspNetCore.Http;

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

    public static readonly Error SameVehicleSwitch = new(
    "HousingMember.SameVehicleSwitch",
    "Cannot switch to the same vehicle",
    StatusCodes.Status400BadRequest
);

    public static readonly Error NoCurrentVehicle = new(
        "HousingMember.NoCurrentVehicle",
        "Rider does not have a current vehicle to switch from",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error NewVehicleNotAvailable = new(
        "HousingMember.NewVehicleNotAvailable",
        "The requested new vehicle is not available",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error PendingSwitchRequest = new(
        "HousingMember.PendingSwitchRequest",
        "There is already a pending switch request for this rider",
        StatusCodes.Status400BadRequest
    );

    // Add these error definitions to the HousingMemberErrors class

    public static readonly Error InsufficientInventory = new(
        "HousingMember.InsufficientInventory",
        "Insufficient quantity in housing inventory",
        StatusCodes.Status400BadRequest
    );

    public static readonly Error DestinationHousingNotFound = new(
        "HousingMember.DestinationHousingNotFound",
        "Destination housing not found",
        StatusCodes.Status404NotFound
    );

    public static readonly Error ItemNotFoundInInventory = new(
        "HousingMember.ItemNotFoundInInventory",
        "Item not found in your housing inventory",
        StatusCodes.Status404NotFound
    );

    public static readonly Error EmptyTransfer = new(
        "HousingMember.EmptyTransfer",
        "Transfer must contain at least one item",
        StatusCodes.Status400BadRequest
    );
    public static readonly Error RiderNotFound = new(
    "HousingMember.RiderNotFound",
    "Rider not found in your housing",
    StatusCodes.Status404NotFound
);

    public static readonly Error CompanyNotFound = new(
        "HousingMember.CompanyNotFound",
        "Company not found",
        StatusCodes.Status404NotFound
    );

    public static readonly Error SameCompanyAssignment = new(
        "HousingMember.SameCompanyAssignment",
        "Rider is already assigned to this company",
        StatusCodes.Status400BadRequest
    );
}
