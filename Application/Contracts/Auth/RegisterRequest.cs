namespace Application.Contracts.Auth;

public record RegisterRequest
(
    string UserName,
    string Password
    );
