namespace Domain.Entities.Spare;

public class BillItem
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public Bill Bill { get; set; } = default!;

    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public BillItemType ItemType { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal OldPrice { get; set; }
    public bool PriceChanged { get; set; }
    public decimal? NewAveragePrice { get; set; }
    public decimal LineTotal { get; set; }
}

public enum BillItemType
{
    SparePart = 1,
    Accessory = 2
}