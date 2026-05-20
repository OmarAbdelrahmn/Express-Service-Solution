// Application/Service/AI/AiConfirmationStore.cs
using Microsoft.Extensions.Caching.Memory;

namespace Application.Service.AI;

public interface IAiConfirmationStore
{
    string Store(string actionType, string argsJson);
    PendingActionDetails? Pop(string token);
}

public class AiConfirmationStore(IMemoryCache cache) : IAiConfirmationStore
{
    public string Store(string actionType, string argsJson)
    {
        var token = Guid.NewGuid().ToString();
        cache.Set(token, new PendingActionDetails(actionType, argsJson),
            TimeSpan.FromMinutes(10));
        return token;
    }

    public PendingActionDetails? Pop(string token)
    {
        if (!cache.TryGetValue<PendingActionDetails>(token, out var pending))
            return null;
        cache.Remove(token);
        return pending;
    }
}

public record PendingActionDetails(string ActionType, string ArgsJson);



public record AiChatResponse(
    string Message,
    object? Data = null,
    bool NeedsConfirmation = false,
    AiPendingAction? PendingAction = null
);

public record AiPendingAction(
    string Token,
    string ActionType,
    string Description,
    object? Preview = null
);

public record AiChatRequest(
    string Message,
    List<AiChatMessage>? History = null,
    string? ConfirmationToken = null
);

public record AiChatMessage(
    string Role,    // "user" or "model"
    string Content
);