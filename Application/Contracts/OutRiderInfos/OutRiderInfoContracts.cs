namespace Application.Contracts.OutRiderInfos;

public record CreateOutRiderInfoRequest(
    string RiderId,
    string? Name,
    string PhoneNumber
);

public record UpdateOutRiderInfoRequest(
    string RiderId,
    string? Name,
    string PhoneNumber
);

public record OutRiderInfoResponse(
    int Id,
    string RiderId,
    string? Name,
    string PhoneNumber,
    DateTime CreatedAt,
    string? CreatedBy
);
