namespace Application.Contracts.Auth;
public record AuthResponse
(
    string Id,
    string UserName,
    string Token,
    int ExpiresIn
    );
