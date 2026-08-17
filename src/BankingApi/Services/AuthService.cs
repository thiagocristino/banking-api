using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BankingApi.Data;
using BankingApi.DTOs;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BankingApi.Services;

public class AuthService
{
    private readonly BankingDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthService(
        BankingDbContext db,
        IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Email == request.Email);

        if (account is null)
        {
            throw new BusinessException(
                "INVALID_CREDENTIALS",
                "Invalid email or password.",
                401);
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(
            request.Password,
            account.PasswordHash);

        if (!passwordValid)
        {
            throw new BusinessException(
                "INVALID_CREDENTIALS",
                "Invalid email or password.",
                401);
        }

        var jwtSettings =
            _configuration
                .GetSection("Jwt")
                .Get<Authentication.JwtSettings>();

        if (jwtSettings is null)
        {
            throw new InvalidOperationException(
                "JWT configuration is missing.");
        }

        var expiresAt =
            DateTime.UtcNow.AddMinutes(
                jwtSettings.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                account.Id.ToString()),

            new(
                JwtRegisteredClaimNames.Email,
                account.Email),

            new(
                "account_id",
                account.Id.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.Key));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSettings.Issuer,
            audience: jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenString =
            new JwtSecurityTokenHandler()
                .WriteToken(token);

        return new LoginResponse
        {
            Token = tokenString,
            ExpiresAt = expiresAt
        };
    }
}