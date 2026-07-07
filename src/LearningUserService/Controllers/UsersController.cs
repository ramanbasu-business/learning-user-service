using learning_user_service.Models.DTOs;
using learning_user_service.Repositories;
using learning_user_service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace learning_user_service.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(
    IUserService userService,
    IRoleService roleService,
    IRoleRepository roleRepository,
    ILogger<UsersController> logger) : ControllerBase
{
    private const string DefaultRoleName = "readonly";


    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Received request to fetch all users");

        var result = await userService.GetUsersAsync(ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(
                detail: string.Join("; ", result.Errors.Select(e => e.Message)),
            statusCode: StatusCodes.Status500InternalServerError);
    }



    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserById(Guid id, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Received request to fetch user {UserId}", id);

        var result = await userService.GetUserByIdAsync(id, ct);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        if (result.Errors.Any(e => e.Metadata.ContainsKey(UserService.NotFoundMetadataKey)))
        {
            return NotFound();
        }

        return Problem(detail: string.Join("; ", result.Errors.Select(e => e.Message)), statusCode: StatusCodes.Status500InternalServerError);
    }

    [HttpPost("auth")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "login request {@Request}",
                new
                {
                    request.Username,
                    Password = "[REDACTED]"
                });

        var result = await userService.LoginAsync(request, ct);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        if (result.Errors.Any(e => e.Metadata.ContainsKey(UserService.NotFoundMetadataKey)))
        {
            return Unauthorized();
        }

        return Problem(
            detail: string.Join("; ", result.Errors.Select(e => e.Message)),
            statusCode: StatusCodes.Status500InternalServerError
        );
    }

    [HttpPost]
    [Authorize(Roles = "admin,manager")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Received request to create user {@Request}",
                new
                {
                    request.Username,
                    request.Email,
                    request.Name,
                    Password = "[REDACTED]"
                });

        var result = await userService.CreateUserAsync(request, ct);

        if (result.IsSuccess)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Created user {UserId} for {Username}", result.Value.Id, request.Username);

            var createdUser = result.Value;
            createdUser = await AssignDefaultRoleToUser(createdUser, ct);

            return CreatedAtAction(nameof(GetUserById), new { id = createdUser.Id }, createdUser);
        }

        return Problem(detail: string.Join("; ", result.Errors.Select(e => e.Message)), statusCode: StatusCodes.Status500InternalServerError);
    }

    private async Task<UserDto> AssignDefaultRoleToUser(UserDto createdUser, CancellationToken ct)
    {
        var defaultRole = await roleRepository.GetByNameAsync(DefaultRoleName, ct);
        if (defaultRole is null)
        {
            logger.LogWarning(
                "Default role {RoleName} not found; skipping role assignment for user {UserId}",
                DefaultRoleName, createdUser.Id);
        }
        else
        {
            var assignResult = await roleService.AssignRoleToUserAsync(
                new AssignRoleRequest(createdUser.Id, defaultRole.Id),
                ct
            );

            if (assignResult.IsSuccess)
            {
                var refetched = await userService.GetUserByIdAsync(createdUser.Id, ct);
                if (refetched.IsSuccess)
                {
                    createdUser = refetched.Value;
                }
            }
            else
            {
                logger.LogWarning(
                    "Failed to assign default role {RoleName} to user {UserId}: {Errors}",
                    DefaultRoleName, createdUser.Id, string.Join("; ", assignResult.Errors.Select(e => e.Message)));
            }
        }
        return createdUser;
    }


    [HttpGet("{id:guid}/roles")]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserRoles(Guid id, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Received request to fetch roles for user {UserId}", id);

        var result = await userService.GetUserByIdAsync(id, ct);

        if (!result.IsSuccess)
        {
            if (result.Errors.Any(e => e.Metadata.ContainsKey(UserService.NotFoundMetadataKey)))
            {
                return NotFound();
            }

            return Problem(detail: string.Join("; ", result.Errors.Select(e => e.Message)), statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(result.Value.Roles);
    }




    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")] /* only admin can delete users */
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Received request to delete user {UserId}", id);

        var result = await userService.DeleteUserAsync(id, ct);

        if (result.IsSuccess)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Deleted user {UserId}", id);

            return NoContent();
        }

        return Problem(detail: string.Join("; ", result.Errors.Select(e => e.Message)), statusCode: StatusCodes.Status500InternalServerError);
    }
}
