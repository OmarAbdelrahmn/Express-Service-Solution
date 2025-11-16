namespace Application.Contracts.Users;
public record UpdateUserProfileRequest
(
    string FullName,
    string Address
    );