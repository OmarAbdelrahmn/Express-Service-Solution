using Application.Contracts.InventoryAudit;
using Domain.Entities;
using Domain.Entities.Spare;
using System.Text.Json;

namespace Application.Service.InventoryAudit;

public static class InventoryAuditProjection
{
    public static readonly string SparePartEntityType = typeof(Domain.Entities.Spare.SparePart).FullName!;
    public static readonly string RiderAccessoryEntityType = typeof(Domain.Entities.Spare.RiderAccessory).FullName!;

    public static InventoryAuditLogResponse ToResponse(SystemAuditEvent auditEvent)
    {
        var oldValues = ReadValues(auditEvent.OldValuesJson);
        var newValues = ReadValues(auditEvent.NewValuesJson);
        var itemType = auditEvent.EntityType == SparePartEntityType
            ? InventoryItemType.SparePart
            : InventoryItemType.RiderAccessory;

        return new InventoryAuditLogResponse(
            auditEvent.Id,
            itemType.ToString(),
            ReadEntityId(auditEvent.EntityKey),
            auditEvent.EntityDisplayName ?? ReadString(newValues, "Name") ?? ReadString(oldValues, "Name") ?? string.Empty,
            auditEvent.Action.ToString(),
            auditEvent.ScopeBefore ?? ReadString(oldValues, "Location"),
            auditEvent.ScopeAfter ?? ReadString(newValues, "Location"),
            ReadInt(oldValues, "Quantity"),
            ReadInt(newValues, "Quantity"),
            ReadDecimal(oldValues, "Price"),
            ReadDecimal(newValues, "Price"),
            auditEvent.ActorName,
            auditEvent.OccurredAtUtc.UtcDateTime,
            auditEvent.OperationName);
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>();

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? new Dictionary<string, JsonElement>();
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out var value) && value.TryGetDecimal(out var result) ? result : null;

    private static int ReadEntityId(string entityKey)
    {
        const string prefix = "Id=";
        var idPart = entityKey.Split('|').FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        return idPart is not null && int.TryParse(idPart[prefix.Length..].Trim('"'), out var id) ? id : 0;
    }
}
