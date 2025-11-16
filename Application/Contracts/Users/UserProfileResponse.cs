namespace Application.Contracts.Users;

public record UserProfileResponse
(
    string UserName,
    string FullName,
    string Address
    );
