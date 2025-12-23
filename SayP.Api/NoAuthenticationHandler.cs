using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SayP.Api;

/// <summary>
/// No-op authentication handler that always succeeds
/// Used for webhook endpoints that don't require authentication
/// </summary>
public class NoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public NoAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "Anonymous") };
        var identity = new ClaimsIdentity(claims, "NoAuth");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "NoAuth");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
