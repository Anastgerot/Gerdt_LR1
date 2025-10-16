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
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var login = (dto.Login ?? "").Trim();
        var password = dto.Password ?? "";

        if (string.Equals(login, "admin", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "This login is reserved." });

        // проверяем, что логин свободен
        var exists = await _db.Users.AnyAsync(u => u.Login == login);
        if (exists) return Conflict(new { message = "Login already exists." });

        var user = new User { Login = login };
        user.SetPassword(password);      // хэшируем пароль
        // Points = 0 по умолчанию (см. конфигурацию/модель)

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Created($"/api/users/{login}", new
        {
            login = user.Login,
            points = user.Points
        });

    }

    [HttpPost]
    public async Task<IActionResult> GetToken([FromBody] LoginData ld)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == ld.login);
        if (user is null || !user.CheckPassword(ld.password))
            return Unauthorized(new { message = "wrong login/password" });

        return Ok(AuthOptions.GenerateToken(user.IsAdmin, user.Login));
    }

    [HttpGet("stats/me")]
    [Authorize]
    public async Task<IActionResult> MyStats()
    {
        var login = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

        var user = await _db.Users.FirstAsync(u => u.Login == login);

        // Базовый запрос БЕЗ Include
        var qBase = _db.UserAssignments.Where(x => x.UserLogin == login);

        // Агрегаты
        var total = await qBase.CountAsync();
        var solved = await qBase.CountAsync(x => x.IsSolved);
        var unsolved = total - solved;

        var attemptsTotal = await qBase.SumAsync(x => (int?)x.Attempts) ?? 0;

        var avgAttemptsSolved = await qBase
            .Where(x => x.IsSolved && x.Attempts > 0)
            .Select(x => (double?)x.Attempts)
            .AverageAsync() ?? 0;

        var lastSolvedAt = await qBase.MaxAsync(x => (DateTime?)x.SolvedAt);
        var lastAnsweredAt = await qBase.MaxAsync(x => (DateTime?)x.LastAnsweredAt);

        // Разбивка по доменам (enum -> string)
        var byDomain = await qBase
            .GroupBy(x => x.Assignment!.Term!.Domain.ToString())
            .Select(g => new
            {
                domain = g.Key,
                solved = g.Count(x => x.IsSolved),
                unsolved = g.Count(x => !x.IsSolved),

                // среднее число попыток по группе; если попыток нет — 0
                avgAttempts = g.Where(x => x.Attempts > 0)
                               .Average(x => (double?)x.Attempts) ?? 0
            })
            .OrderByDescending(x => x.solved)
            .ToListAsync();

        // Топ сложных (больше попыток среди нерешённых)
        var hardest = await qBase
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

        // Топ решённых с наибольшим числом попыток
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
                avgAttemptsSolved,
                lastSolvedAt,
                lastAnsweredAt
            },

            byDomain,
            hardest,
            mostAttemptsSolved
        });
    }

}
