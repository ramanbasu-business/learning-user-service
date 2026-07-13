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


// Create a WebApplicationBuilder instance to configure services and middleware
var builder = WebApplication.CreateBuilder(args);


// Add OpenApi and Controllers to the service collection for API documentation and request handling
builder.Services.AddOpenApi();
builder.Services.AddControllers();


// Configure JWT Bearer authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");



// Configure authentication services for the application
builder.Services
    // Specify that we are using JWT Bearer authentication scheme
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    // Configure JWT Bearer options
    .AddJwtBearer(options =>
    {
        // Define how incoming JWT tokens should be validated
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Ensure the token has a valid signing key
            ValidateIssuerSigningKey = true,

            // Provide the symmetric security key used to sign the JWT
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),

            // Skip issuer validation (since we are not restricting who issues the token)
            ValidateIssuer = false,

            // Skip audience validation (since we are not restricting who can consume the token)
            ValidateAudience = false,

            // Ensure the token has not expired
            ValidateLifetime = true,

            // Map the "roles" claim in the JWT to ASP.NET Core's role system
            RoleClaimType = "roles",

            // Map the "sub" claim in the JWT to ASP.NET Core's user identity name
            NameClaimType = "sub",
        };
    });


// Configure authorization services for the application
builder.Services.AddAuthorization();



// 1. Configure Serilog to write to Console and a daily rolling .txt file
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

// 2. Instruct the Web API host to replace default logger with Serilog
builder.Host.UseSerilog();


// Configure PostgreSQL database connection
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")!;


// Add the AppDbContext to the service collection with PostgreSQL provider and snake_case naming convention
builder.Services.AddDbContext<AppDbContext>(options =>
    options
    .UseNpgsql(postgresConnectionString)
    .UseSnakeCaseNamingConvention() // case-insensitive naming convention for PostgreSQL
);

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IRoleService, RoleService>();


// Add health checks to monitor the application's health, including PostgreSQL database connectivity
builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConnectionString);

// Add ProblemDetails middleware to provide standardized error responses
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Add CORS policy to allow requests from any origin, method, and header
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // browsable Swagger-style UI at /scalar/v1
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}


// Configure the HTTP request pipeline with middleware components
app.UseExceptionHandler();

// Enable CORS with a permissive policy (allowing any origin, method, and header)
app.UseHttpsRedirection();

// Add Serilog request logging middleware to log HTTP requests and responses
app.UseSerilogRequestLogging(); // This will log HTTP requests and responses, including status codes and execution times


// Add ProblemDetails middleware to handle exceptions and return standardized error responses
app.UseAuthentication(); // Authentication comes first

// Add authorization middleware to enforce access control based on user roles and policies
app.UseAuthorization(); // Then authorization


// Map controller routes to handle incoming HTTP requests
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
