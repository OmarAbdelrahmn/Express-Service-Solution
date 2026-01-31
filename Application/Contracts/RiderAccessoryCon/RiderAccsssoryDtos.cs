using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.RiderAccessoryCon;

internal class RiderAccsssoryDtos
{

}


public record RiderAccessoryRequest(
    string Name,
    int Quantity,
    decimal Price,
    string Location
);

public record RiderAccessoryResponse(
    int Id,
    string Name,
    int Quantity,
    int Available,
    decimal Price,
    string Location,
    DateTime CreatedAt
);

public record IssueAccessoryRequest(
    int RiderId
);


public record RiderAccessoryUsageResponse(
    int Id,
    int RiderAccessoryId,
    string AccessoryName,
    int RiderId,
    string RiderNameEN,
    string RiderNameAR,
    DateTime IssuedAt,
    decimal? Cost
);