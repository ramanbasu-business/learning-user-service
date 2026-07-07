using System.Security.Cryptography;
using System.Text;
using FluentResults;
using learning_user_service.Models.DTOs;
using learning_user_service.Models.Entities;
using learning_user_service.Repositories;

namespace learning_user_service.Services;

public class UserService : IUserService
{
    public const string DefaultRoleName = "ReadOnly";

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IRoleService roleService,
        ILogger<UserService> logger)
    {
        this.logger = logger;
        this.userRepository = userRepository;
        this.roleRepository = roleRepository;
        this.roleService = roleService;
    }

    public const string NotFoundMetadataKey = "NotFound";
    private readonly IUserRepository userRepository;
    private readonly IRoleRepository roleRepository;
    private readonly IRoleService roleService;
    private readonly ILogger<UserService> logger;


    public async Task<Result<IReadOnlyList<UserDto>>> GetUsersAsync(CancellationToken ct = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetching all users");

            var users = await userRepository.GetAllAsync(ct);
            var userDtos = users.Select(MapToDto).ToList();

            return Result.Ok((IReadOnlyList<UserDto>)userDtos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch users");
            return Result.Fail<IReadOnlyList<UserDto>>(new Error("Failed to fetch users").CausedBy(ex));
        }
    }

    public async Task<Result<UserDto>> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Fetching user {UserId}", id);

            var user = await userRepository.GetByIdAsync(id, ct);
            if (user is null)
            {
                return Result.Fail<UserDto>(new Error($"User {id} not found").WithMetadata(NotFoundMetadataKey, true));
            }

            return Result.Ok(MapToDto(user));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch user {UserId}", id);
            return Result.Fail<UserDto>(new Error("Failed to fetch user").CausedBy(ex));
        }
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Creating user {Username}", request.Username);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password),
                Name = request.Name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                EntraId = request.EntraId
            };

            var createdUser = await userRepository.CreateAsync(user, ct);
            return Result.Ok(MapToDto(createdUser));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create user {Username}", request.Username);
            return Result.Fail<UserDto>(new Error("Failed to create user").CausedBy(ex));
        }
    }

    public async Task<Result<UserDto>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Attempting login for user {Username}", request.Username);

            var user = await userRepository.GetByUsernameAsync(request.Username, ct);
            if (user is null)
            {
                return Result.Fail<UserDto>(new Error("Invalid username or password").WithMetadata(NotFoundMetadataKey, true));
            }

            var passwordHash = HashPassword(request.Password);
            if (user.PasswordHash != passwordHash)
            {
                return Result.Fail<UserDto>(new Error("Invalid username or password").WithMetadata(NotFoundMetadataKey, true));
            }

            return Result.Ok(MapToDto(user));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to authenticate user {Username}", request.Username);
            return Result.Fail<UserDto>(new Error("Authentication failed").CausedBy(ex));
        }
    }

    public async Task<Result<bool>> DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Deleting user {UserId}", id);
            await userRepository.DeleteAsync(id, ct);
            return Result.Ok(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete user {UserId}", id);
            return Result.Fail<bool>(new Error("Failed to delete user").CausedBy(ex));
        }
    }

    private static UserDto MapToDto(User user) =>
        new(
            user.Id,
            user.Username,
            user.Email ?? string.Empty,
            user.EntraId,
            user.Name,
            user.CreatedAt,
            user.UpdatedAt,
            user.IsActive,
            user.LastLogin,
        user.UserRoles.Select(ur => new RoleDto(ur.Role.Id, ur.Role.Name)).ToList());

    private static string HashPassword(string password)
    {
        var hashedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}
