using Gerdt_LR1.Auth;
using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gerdt_LR1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly AppDbContext _db;
    public AccountController(AppDbContext db) => _db = db;

    public struct LoginData
    {
        public string login { get; set; }
        public string password { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> GetToken([FromBody] LoginData ld)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == ld.login);
        if (user is null || !user.CheckPassword(ld.password))
            return Unauthorized(new { message = "wrong login/password" });

        return Ok(AuthOptions.GenerateToken(user.IsAdmin, user.Login));
    }

}
