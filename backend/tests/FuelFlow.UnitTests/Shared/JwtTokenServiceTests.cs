using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FuelFlow.UnitTests.Shared;

public class JwtTokenServiceTests
{
    private const string Secret = "SuperSecretKeyForUnitTestsThatIsDefinitelyLongEnoughForHmacSha256";
    private const string Issuer = "https://fuelflow.test";
    private const string Audience = "https://fuelflow.test/api";
    private const int ExpirationMinutes = 15;

    private static JwtTokenService CreateService(int expirationMinutes = ExpirationMinutes)
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenExpirationMinutes = expirationMinutes
        });

        return new JwtTokenService(options);
    }

    [Fact]
    public void GenerateAccessToken_ShouldProduceValidJwt()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        const string phoneNumber = "+380991234567";
        const string role = "Admin";

        var token = service.GenerateAccessToken(userId, phoneNumber, role);

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(token).Should().BeTrue();

        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().Contain(Audience);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == userId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "phone_number" && c.Value == phoneNumber);
        jwt.Claims.Should().Contain(c => c.Type == "token_version" && c.Value == "1");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == role);

        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ClockSkew = TimeSpan.Zero
        }, out _);

        principal.Should().NotBeNull();
    }

    [Fact]
    public void GenerateAccessToken_ShouldIncludeRoleClaim_WhenProvided()
    {
        var service = CreateService();

        var token = service.GenerateAccessToken(Guid.NewGuid(), "+380991234567", "StationOwner");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "StationOwner");
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueTokens()
    {
        var service = CreateService();

        var first = service.GenerateRefreshToken();
        var second = service.GenerateRefreshToken();

        first.Should().NotBe(second);
        first.Should().HaveLength(88);
    }

    [Fact]
    public void GenerateAccessToken_ShouldExpireAfterConfiguredMinutes()
    {
        const int minutes = 30;
        var service = CreateService(minutes);

        var token = service.GenerateAccessToken(Guid.NewGuid(), "+380991234567", null);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(minutes), TimeSpan.FromSeconds(10));
    }
}
