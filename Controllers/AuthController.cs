using System.ComponentModel.DataAnnotations;
using Gerdt_LR1.Auth;
using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.AspNetCore.Authorization;
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

    public class RegisterDto
    {
        [Required, MinLength(3), MaxLength(64)]
        public string Login { get; set; } = "";

        [Required, MinLength(3), MaxLength(128)]
        public string Password { get; set; } = "";
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto? dto)
    {
        try
        {
            var login = (dto.Login ?? "").Trim();
            var password = dto.Password ?? "";

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                return BadRequest(new { message = "Login and password are required." });

            if (string.Equals(login, "admin", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = "This login is reserved." });

            var exists = await _db.Users.AnyAsync(u => u.Login == login);
            if (exists)
                return Conflict(new { message = "Login already exists." });

            var user = new User { Login = login };
            user.SetPassword(password); 

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Created($"/api/users/{login}", new
            {
                login = user.Login,
                points = user.Points
            });
        }

        catch (Exception ex)
        {
            return Problem(title: "Unexpected server error while registering.", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetToken([FromBody] LoginData ld)
    {
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == ld.login);
            if (user is null || !user.CheckPassword(ld.password))
                return Unauthorized(new { message = "Invalid login or password." });

            return Ok(AuthOptions.GenerateToken(user.IsAdmin, user.Login));
        }

        catch (Exception ex)
        {
            return Problem(title: "Unexpected server error while issuing token.", detail: ex.Message, statusCode: 500);
        }

    }

    [HttpGet("stats/me")]
    [Authorize]
    public async Task<IActionResult> MyStats()
    {
        try
        {
            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == login);
            if (user is null)
                return NotFound(new { message = "User not found." });

            var qBase = _db.UserAssignments.Where(x => x.UserLogin == login);

            var total = await qBase.CountAsync();
            var solved = await qBase.CountAsync(x => x.IsSolved);
            var unsolved = total - solved;
            var attemptsTotal = await qBase.SumAsync(x => (int?)x.Attempts) ?? 0;
            var lastSolvedAt = await qBase.MaxAsync(x => (DateTime?)x.SolvedAt);
            var lastAnsweredAt = await qBase.MaxAsync(x => (DateTime?)x.LastAnsweredAt);


            var byDomain = await qBase
                .GroupBy(x => x.Assignment!.Term!.Domain.ToString())
                .Select(g => new
                {
                    domain = g.Key,
                    solved = g.Count(x => x.IsSolved),
                    unsolved = g.Count(x => !x.IsSolved),
                })
                .OrderByDescending(x => x.solved)
                .ToListAsync();


            var hardestUnsolved = await qBase
                .Where(x => !x.IsSolved && x.Attempts > 0)
                .OrderByDescending(x => x.Attempts)
                .Take(5)
                .Select(x => new
                {
                    assignmentId = x.AssignmentId,
                    termId = x.Assignment!.TermId,
                    question = x.Assignment.Direction == Direction.EnToRu
                               ? x.Assignment.Term!.En
                               : x.Assignment.Term!.Ru,
                    attempts = x.Attempts
                })
                .ToListAsync();


            var mostAttemptsSolved = await qBase
                .Where(x => x.IsSolved)
                .OrderByDescending(x => x.Attempts)
                .Take(5)
                .Select(x => new
                {
                    assignmentId = x.AssignmentId,
                    termId = x.Assignment!.TermId,
                    question = x.Assignment.Direction == Direction.EnToRu
                               ? x.Assignment.Term!.En
                               : x.Assignment.Term!.Ru,
                    attempts = x.Attempts,
                    solvedAt = x.SolvedAt
                })
                .ToListAsync();

            return Ok(new
            {
                user = user.Login,
                points = user.Points,
                totals = new
                {
                    assignments = total,
                    solved,
                    unsolved,
                    attemptsTotal,
                    lastSolvedAt,
                    lastAnsweredAt
                },
                byDomain,
                hardestUnsolved,
                mostAttemptsSolved
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "Unexpected server error while building stats.", detail: ex.Message, statusCode: 500);
        }
    }

}
