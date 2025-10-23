using Gerdt_LR1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gerdt_LR1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly IAccountService _svc;
    public AccountController(IAccountService svc) => _svc = svc;

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto? dto, CancellationToken ct)
    {
        try
        {
            if (dto is null) 
                return BadRequest(new { message = "Body is required." });

            var (user, error) = await _svc.RegisterAsync(dto, ct);
            if (user is null) 
                return Conflict(new { message = error });

            return Created($"/api/users/{user.Login}", new { login = user.Login, points = user.Points });
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while registering.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost] 
    public async Task<IActionResult> GetToken([FromBody] LoginData ld, CancellationToken ct)
    {
        try
        {
            var token = await _svc.GetTokenAsync(ld, ct);
            if (token is null) 
                return Unauthorized(new { message = "Invalid login or password." });

            return Ok(token);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while issuing token.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpGet("stats/me")]
    [Authorize]
    public async Task<IActionResult> MyStats(CancellationToken ct)
    {
        try
        {
            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login)) 
                return Unauthorized(new { message = "User is not authenticated." });

            var res = await _svc.MyStatsAsync(login, ct);
            if (res is null) 
                return NotFound(new { message = "User not found." });

            return Ok(res);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while building stats.", 
                detail: ex.Message, 
                statusCode: 500); }
    }
}
