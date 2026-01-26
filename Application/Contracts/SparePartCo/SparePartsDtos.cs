using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.SparePartCo;

internal class SparePartsDtos
{
}


public record SparePartRequest(
    string Name,
    int Quantity,
    decimal Price,
    string Location
);

public record SparePartResponse(
    int Id,
    string Name,
    int Quantity,
    decimal Price,
    string Location,
    DateTime CreatedAt
);

public record SparePartUsageRequest(
    string VehicleNumber,
    int QuantityUsed

);

public record SparePartUsageResponse(
    int Id,
    int SparePartId,
    string SparePartName,
    string VehicleNumber,
    int QuantityUsed,
    DateTime UsedAt
);
