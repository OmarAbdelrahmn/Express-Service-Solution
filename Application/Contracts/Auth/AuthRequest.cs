namespace Application.Contracts.Auth;

public record AuthRequest
(
    string UserName,
    string Password
    );
