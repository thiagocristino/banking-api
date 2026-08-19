using System.Text;
using BankingApi.Authentication;
using BankingApi.Data;
using BankingApi.Exceptions;
using BankingApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Controllers
builder.Services.AddControllers();

// ProblemDetails
builder.Services.AddProblemDetails();

// Exception Handlers
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();

// Configuração do JWT
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

var jwtSettings =
    builder.Configuration
        .GetSection("Jwt")
        .Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "JWT configuration is missing.");

// Autenticação JWT
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtSettings.Key))
            };
    });

// Configuração do banco PostgreSQL
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "BankingDatabase")));

// Services
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<ReversalService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Tratamento global de exceções
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Mapeia os Controllers
app.MapControllers();

// Health check do banco
app.MapGet(
    "/health/database",
    async (BankingDbContext db) =>
    {
        var canConnect =
            await db.Database.CanConnectAsync();

        return canConnect
            ? Results.Ok(new
            {
                database = "connected"
            })
            : Results.StatusCode(503);
    });

app.Run();