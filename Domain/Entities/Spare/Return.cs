namespace Domain.Entities.Spare
{
    public class Return
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
        public Supplier Supplier { get; set; } = default!;

        public string? ReturnNumber { get; set; }
        public DateTime ReturnDate { get; set; } = DateTime.UtcNow.AddHours(3);
        public decimal TotalAmount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ProcessedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }

        public ICollection<ReturnItem> ReturnItems { get; set; } = [];
    }

    public class ReturnItem
    {
        public int Id { get; set; }
        public int ReturnId { get; set; }
        public Return Return { get; set; } = default!;

        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public ReturnItemType ItemType { get; set; }

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }

    public enum ReturnItemType
    {
        SparePart = 1,
        Accessory = 2
    }
}