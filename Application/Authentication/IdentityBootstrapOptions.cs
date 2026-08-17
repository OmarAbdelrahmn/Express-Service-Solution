namespace Application.Authentication;

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public string AdminPassword { get; init; } = string.Empty;
    public string MasterPassword { get; init; } = string.Empty;
}
