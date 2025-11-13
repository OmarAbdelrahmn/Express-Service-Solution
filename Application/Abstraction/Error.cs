namespace Application.Abstraction;

public record Error(string Code, string Description, int? StatuesCode)
{
    public static Error non => new(string.Empty, string.Empty, null);
}
