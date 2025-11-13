namespace Application.Contracts.Users;
public record UpdateUserProfileRequest
(
    string FirstName,
    string LastName
    );