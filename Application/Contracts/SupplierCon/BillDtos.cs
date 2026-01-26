using Domain.Entities.Spare;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.SupplierCon;

internal class BillDtos
{
}
public record ReceiveBillRequest(
    int SupplierId,  // Changed from SupplierName
    List<BillItemRequest> Items,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    string? Notes
);

public record BillItemRequest(
    int ItemId,
    BillItemType ItemType,
    int Quantity,
    decimal UnitPrice
);


public record BillResponse(
    int Id,
    int SupplierId,
    string SupplierName,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    decimal TotalAmount,
    int TotalItems,
    string ProcessedBy,
    DateTime ProcessedAt,
    string? Notes,
    List<BillItemResponse> Items
);

public record BillItemResponse(
    int Id,
    int ItemId,
    string ItemName,
    BillItemType ItemType,
    int Quantity,
    decimal UnitPrice,
    decimal OldPrice,
    bool PriceChanged,
    decimal? NewAveragePrice,
    decimal LineTotal
);

public record BillSummaryResponse(
    int Id,
    int SupplierId,
    string SupplierName,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    decimal TotalAmount,
    int TotalItems,
    DateTime ProcessedAt,
    string ProcessedBy
);