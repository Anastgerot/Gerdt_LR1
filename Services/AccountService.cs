using Gerdt_LR1.Auth;
using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using static Gerdt_LR1.Controllers.AccountController;

namespace Gerdt_LR1.Services;

public class AccountService : IAccountService
{
    private readonly AppDbContext _db;
    public AccountService(AppDbContext db) => _db = db;

    public async Task<(User? created, string? error)> RegisterAsync(RegisterDto dto, CancellationToken ct)
    {
        var login = (dto.Login ?? "").Trim();
        var password = dto.Password ?? "";

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return (null, "Login and password are required.");

        if (string.Equals(login, "admin", StringComparison.OrdinalIgnoreCase))
            return (null, "This login is reserved.");

        var exists = await _db.Users.AnyAsync(u => u.Login == login, ct);
        if (exists) return (null, "Login already exists.");

        var user = new User { Login = login };
        user.SetPassword(password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return (user, null);
    }

    public async Task<object?> GetTokenAsync(LoginData ld, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == ld.login, ct);
        if (user is null || !user.CheckPassword(ld.password)) return null;
        return AuthOptions.GenerateToken(user.IsAdmin, user.Login);
    }

    public async Task<object?> MyStatsAsync(string login, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
        if (user is null) return null;

        var qBase = _db.UserAssignments.Where(x => x.UserLogin == login);

        var total = await qBase.CountAsync(ct);
        var solved = await qBase.CountAsync(x => x.IsSolved, ct);
        var unsolved = total - solved;
        var attemptsTotal = await qBase.SumAsync(x => (int?)x.Attempts, ct) ?? 0;
        var lastSolvedAt = await qBase.MaxAsync(x => (DateTime?)x.SolvedAt, ct);
        var lastAnsweredAt = await qBase.MaxAsync(x => (DateTime?)x.LastAnsweredAt, ct);

        var byDomain = await qBase
            .GroupBy(x => x.Assignment!.Term!.Domain.ToString())
            .Select(g => new
            {
                domain = g.Key,
                solved = g.Count(x => x.IsSolved),
                unsolved = g.Count(x => !x.IsSolved),
            })
            .OrderByDescending(x => x.solved)
            .ToListAsync(ct);

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
            .ToListAsync(ct);

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
            .ToListAsync(ct);

        return new
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
        };
    }
}
