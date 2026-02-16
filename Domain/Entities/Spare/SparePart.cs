using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities.Spare;

public class SparePart
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public ICollection<SparePartUsage> SparePartUsages { get; set; } = [];

}
