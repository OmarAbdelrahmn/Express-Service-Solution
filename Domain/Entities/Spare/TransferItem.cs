using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class TransferItem
{
    public int Id { get; set; }
    public int TransferId { get; set; }
    public Transfer Transfer { get; set; } = default!;

    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public TransferItemType ItemType { get; set; }
    public int Quantity { get; set; }
}

public enum TransferItemType
{
    SparePart = 1,
    Accessory = 2
}