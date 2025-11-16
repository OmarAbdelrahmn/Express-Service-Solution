using Domain.Entities;

namespace Application.Contracts.Users;

public record UserResponse
(
    string Id,
    string FullName,
    string Address,
    string UserName,
    bool IsDisable
    );
public record UserResponses
(
    string Id,
    string FullName,
    string Address,
    string UserName,
    bool IsDisable,
    IEnumerable<string> Roles
    );
