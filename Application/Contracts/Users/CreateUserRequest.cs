namespace Application.Contracts.Users;

public record CreateUserRequest
(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    IList<string> Roles
    );
