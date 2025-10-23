using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using static Gerdt_LR1.Controllers.AssignmentsController;

namespace Gerdt_LR1.Services;

public class AssignmentsService : IAssignmentsService
{
    private readonly AppDbContext _db;
    public AssignmentsService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Assignment>> GetAllAsync(CancellationToken ct)
        => await _db.Assignments.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);

    public async Task<Assignment?> GetByIdAsync(int id, CancellationToken ct)
        => await _db.Assignments.FindAsync([id], ct);

    public async Task<IReadOnlyList<object>> GetUserAssignmentsAsync(string login, bool? solved, CancellationToken ct)
    {
        var q = _db.UserAssignments.AsNoTracking().Where(ua => ua.UserLogin == login);
        if (solved.HasValue) q = q.Where(ua => ua.IsSolved == solved.Value);
        q = q.OrderBy(ua => ua.IsSolved);

        return await q.Select(ua => new
        {
            assignmentId = ua.AssignmentId,
            termId = ua.Assignment!.TermId,
            direction = ua.Assignment.Direction.ToString(),
            isSolved = ua.IsSolved,
            solvedAt = ua.SolvedAt,
            question = ua.Assignment.Direction == Direction.EnToRu
                        ? ua.Assignment.Term!.En : ua.Assignment.Term!.Ru,
            expected = ua.IsSolved
                        ? ua.Assignment.Term!.Translate(ua.Assignment.Direction) : null
        }).ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var a = await _db.Assignments.FindAsync([id], ct);
        if (a is null) return false;
        _db.Assignments.Remove(a);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<object?> GetQuestionOrCheckAnswerAsync(int id, string login, AnswerDto? dto, CancellationToken ct)
    {
        var a = await _db.Assignments.Include(x => x.Term).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;

        var ua = await _db.UserAssignments.FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id, ct);
        if (ua is null) return new { forbid = true }; // сигнал контроллеру вернуть 403

        var question = a.Direction == Direction.EnToRu ? a.Term!.En : a.Term!.Ru;

        if (dto is null || string.IsNullOrWhiteSpace(dto.Answer))
        {
            return new
            {
                assignmentId = a.Id,
                termId = a.TermId,
                direction = a.Direction.ToString(),
                question,
                yourAnswer = (string?)null,
                expected = (string?)null,
                correct = (bool?)null,
                isSolved = ua.IsSolved
            };
        }

        ua.Attempts += 1;
        ua.LastAnsweredAt = DateTime.UtcNow;

        var wasSolved = ua.IsSolved;
        var correct = a.CheckAnswer(dto.Answer);

        if (correct && !wasSolved)
        {
            ua.IsSolved = true;
            ua.SolvedAt = DateTime.UtcNow;

            var user = await _db.Users.FindAsync([login], ct);
            user?.AddPoints(1);
        }

        await _db.SaveChangesAsync(ct);

        return new
        {
            assignmentId = a.Id,
            termId = a.TermId,
            direction = a.Direction.ToString(),
            question,
            yourAnswer = dto.Answer,
            expected = a.Term!.Translate(a.Direction),
            correct,
            isSolved = ua.IsSolved
        };
    }
    public async Task<bool> IsLinkedAsync(int assignmentId, string login, CancellationToken ct)
    {
        return await _db.UserAssignments
            .AnyAsync(x => x.UserLogin == login && x.AssignmentId == assignmentId, ct);
    }

    public async Task<object> SwitchDirectionAsync(int id, string login, CancellationToken ct)
    {
        var a = await _db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return new { notFound = true };

        var newDir = a.Direction == Direction.EnToRu ? Direction.RuToEn : Direction.EnToRu;

        var opposite = await _db.Assignments.FirstOrDefaultAsync(
            x => x.TermId == a.TermId && x.Direction == newDir, ct);

        if (opposite is null)
        {
            opposite = new Assignment { TermId = a.TermId, Direction = newDir };
            _db.Assignments.Add(opposite);
            await _db.SaveChangesAsync(ct);
        }

        var uaCurrent = await _db.UserAssignments
            .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id, ct);

        var uaOpposite = await _db.UserAssignments
            .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == opposite.Id, ct);

        if (uaOpposite is not null)
        {
            uaOpposite.IsSolved = false;
            uaOpposite.SolvedAt = null;
        }
        else if (uaCurrent is not null)
        {
            uaCurrent.AssignmentId = opposite.Id;
            uaCurrent.IsSolved = false;
            uaCurrent.SolvedAt = null;
        }
        else
        {
            return new { conflict = true };
        }

        await _db.SaveChangesAsync(ct);

        return new
        {
            assignmentId = opposite.Id,
            termId = opposite.TermId,
            newDirection = newDir.ToString(),
            isSolved = false
        };
    }

    public async Task<(object result, int createdStatus)> CreateForUserAsync(string login, AssignForMeDto dto, CancellationToken ct)
    {
        var term = await _db.Terms.FindAsync([dto.TermId], ct);
        if (term is null) return (new { notFound = true, msg = $"Term with id={dto.TermId} not found." }, 404);

        var dir = dto.Direction ?? Direction.EnToRu;

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.TermId == term.Id && a.Direction == dir, ct);

        if (assignment is null)
        {
            assignment = new Assignment { TermId = term.Id, Direction = dir };
            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync(ct);
        }

        var linkExists = await _db.UserAssignments
            .AnyAsync(ua => ua.UserLogin == login && ua.AssignmentId == assignment.Id, ct);

        if (linkExists)
            return (new { conflict = true, msg = "This assignment is already linked to the current user." }, 409);

        _db.UserAssignments.Add(new UserAssignment
        {
            UserLogin = login,
            AssignmentId = assignment.Id,
            IsSolved = false
        });
        await _db.SaveChangesAsync(ct);

        var question = dir == Direction.EnToRu ? term.En : term.Ru;

        return (new
        {
            assignmentId = assignment.Id,
            termId = term.Id,
            direction = dir.ToString(),
            question
        }, 201);
    }

    public async Task<(IEnumerable<object> items, int requested, int created)> GenerateAsync(GenerateAssignmentsDto dto, CancellationToken ct)
    {
        var dir = dto.Direction;

        var candidateIds = await _db.Terms
            .Where(t => !_db.Assignments.Any(a => a.TermId == t.Id && a.Direction == dir))
            .OrderBy(t => t.Id)
            .Take(dto.Count)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0)
            return (Enumerable.Empty<object>(), dto.Count, 0);

        var toCreate = candidateIds.Select(id => new Assignment { TermId = id, Direction = dir }).ToList();

        _db.Assignments.AddRange(toCreate);
        await _db.SaveChangesAsync(ct);

        var items = toCreate.Select(a => new { assignmentId = a.Id, termId = a.TermId, direction = a.Direction.ToString() });
        return (items, dto.Count, toCreate.Count);
    }

    public async Task<(IEnumerable<object> items, int createdLinks)> AddAssignmentsToUserAsync(AddAssignmentsDto dto, CancellationToken ct)
    {
        var login = dto.UserLogin.Trim();
        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Login == login, ct);
        if (!exists) return (Enumerable.Empty<object>(), 0);

        var candidateAssignments = await _db.Assignments.AsNoTracking()
            .Where(a => !_db.UserAssignments.Any(ua => ua.UserLogin == login && ua.AssignmentId == a.Id))
            .OrderBy(a => a.Id)
            .Take(dto.Count)
            .Select(a => new { a.Id, a.TermId, a.Direction })
            .ToListAsync(ct);

        if (candidateAssignments.Count == 0)
            return (Enumerable.Empty<object>(), 0);

        var links = candidateAssignments.Select(a => new UserAssignment
        {
            UserLogin = login,
            AssignmentId = a.Id,
            IsSolved = false
        }).ToList();

        _db.UserAssignments.AddRange(links);
        await _db.SaveChangesAsync(ct);

        var items = candidateAssignments.Select(a => new
        {
            assignmentId = a.Id,
            termId = a.TermId,
            direction = a.Direction.ToString()
        });

        return (items, links.Count);
    }

    public async Task<object?> MarkUnsolvedAsync(int id, string login, ResetAssignmentDto? dto, CancellationToken ct)
    {
        var a = await _db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;

        var ua = await _db.UserAssignments.FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id, ct);
        if (ua is null) return new { forbid = true };

        ua.IsSolved = false;
        ua.SolvedAt = null;

        var reset = dto ?? new ResetAssignmentDto();
        if (reset.ResetAttempts) ua.Attempts = 0;
        if (reset.ClearTimestamps) ua.LastAnsweredAt = null;

        await _db.SaveChangesAsync(ct);

        return new
        {
            assignmentId = ua.AssignmentId,
            user = ua.UserLogin,
            isSolved = ua.IsSolved,
            attempts = ua.Attempts,
            solvedAt = ua.SolvedAt,
            lastAnsweredAt = ua.LastAnsweredAt
        };
    }
}
