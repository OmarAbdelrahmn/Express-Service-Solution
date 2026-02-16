namespace Domain.Entities.Spare;

public class Bill
{
    public int Id { get; set; }

    public int SupplierId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string ProcessedBy { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }

    public ICollection<BillItem> BillItems { get; set; } = [];
    public Supplier Supplier { get; set; } = default!;

}