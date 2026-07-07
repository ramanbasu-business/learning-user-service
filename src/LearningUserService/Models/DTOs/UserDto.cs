namespace learning_user_service.Models.DTOs;

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string? Name,
    string? EntraId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsActive,
    DateTime? LastLogin,
    IReadOnlyList<RoleDto> Roles);
public record CreateUserRequest(
    string Username,
    string Email,
    string Password,
    string? Name = null,
    string? EntraId = null
);
public record LoginRequest(string Username, string Password);
public record UpdateUserRequest(string? Name, string? Email);
public record RoleDto(Guid Id, string Name);
public record AssignRoleRequest(Guid UserId, Guid RoleId);
