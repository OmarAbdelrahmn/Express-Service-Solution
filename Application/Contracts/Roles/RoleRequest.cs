namespace Application.Contracts.Roles;

public record RoleRequest
(
    string OldName,
    string NewName
    //IList<string> Permissions
    );
