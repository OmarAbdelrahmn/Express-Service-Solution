using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class Transfer
{
    public int Id { get; set; }
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public int HousingId { get; set; }
    public string TransferredBy { get; set; } = string.Empty;
    public DateTime TransferredAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public ICollection<TransferItem> TransferItems { get; set; } = [];
}
