using System.Text;
using learning_user_service.Data;
using learning_user_service.Infrastructure;
using learning_user_service.Repositories;
using learning_user_service.Services;

using Microsoft.EntityFrameworkCore;

using Scalar.AspNetCore;
using Serilog;

using Microsoft.AspNetCore.Authentication.JwtBearer; // JWT Bearer authentication
using Microsoft.IdentityModel.Tokens; // JWT Bearer authentication

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            RoleClaimType = "roles",
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

// 1. Configure Serilog to write to Console and a daily rolling .txt file
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
// 2. Instruct the Web API host to replace default logger with Serilog
builder.Host.UseSerilog();



var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")!;

builder.Services.AddDbContext<AppDbContext>(options =>
    options
    .UseNpgsql(postgresConnectionString)
    .UseSnakeCaseNamingConvention() // case-insensitive naming convention for PostgreSQL
);

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IRoleService, RoleService>();

builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnectionString);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // browsable Swagger-style UI at /scalar/v1
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseSerilogRequestLogging(); // This will log HTTP requests and responses, including status codes and execution times

// ----- auth -----
app.UseAuthentication(); // Authentication comes first
app.UseAuthorization(); // Then authorization


app.MapControllers();
app.MapHealthChecks("/health");


try
{
    Log.Information("Starting up the Web API execution pipeline.");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "The application host terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush(); // Safely flushes remaining log buffers before shutting down
}
