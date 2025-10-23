using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using static Gerdt_LR1.Controllers.TermsController;

namespace Gerdt_LR1.Services;

public class TermsService : ITermsService
{
    private readonly AppDbContext _db;
    public TermsService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Term>> GetAllAsync(CancellationToken ct)
        => await _db.Terms.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);

    public async Task<Term?> GetByIdAsync(int id, CancellationToken ct)
        => await _db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<(bool ok, string? conflictMsg)> UpdateAsync(int id, Term input, CancellationToken ct)
    {
        var existing = await _db.Terms.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is null) return (ok: false, conflictMsg: null);

        existing.En = input.En?.Trim() ?? existing.En;
        existing.Ru = input.Ru?.Trim() ?? existing.Ru;
        existing.Domain = input.Domain;

        try
        {
            await _db.SaveChangesAsync(ct);
            return (true, null);
        }
        catch (DbUpdateException ex)
        {
            return (false, "Term with the same EN/RU already exists.");
        }
    }

    public async Task<(Term? created, string? conflictMsg)> CreateAsync(Term input, CancellationToken ct)
    {
        input.En = input.En?.Trim() ?? "";
        input.Ru = input.Ru?.Trim() ?? "";

        var dup = await _db.Terms.AnyAsync(t => t.En == input.En && t.Ru == input.Ru, ct);
        if (dup) return (null, "Term with the same EN/RU already exists.");

        _db.Terms.Add(input);
        await _db.SaveChangesAsync(ct);
        return (input, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var term = await _db.Terms.FindAsync([id], ct);
        if (term is null) return false;
        _db.Terms.Remove(term);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<object?> TranslateAndRememberAsync(string login, TranslateDto dto, CancellationToken ct)
    {
        var text = (dto.Text ?? "").Trim();
        var textLower = text.ToLowerInvariant();

        var term = await _db.Terms.FirstOrDefaultAsync(
            t => t.En.ToLower() == textLower || t.Ru.ToLower() == textLower, ct);

        if (term is null) return null;

        var direction = dto.Direction ?? (HasCyrillic(text) ? Direction.RuToEn : Direction.EnToRu);

        var question = direction == Direction.EnToRu ? term.En : term.Ru;
        var translation = direction == Direction.EnToRu ? term.Ru : term.En;

        // 1) история просмотров пользователя
        var link = await _db.UserTerms.FirstOrDefaultAsync(x => x.UserLogin == login && x.TermId == term.Id, ct);
        if (link is null)
        {
            _db.UserTerms.Add(new UserTerm { UserLogin = login, TermId = term.Id, LastViewedAt = DateTime.UtcNow });
        }
        else
        {
            link.LastViewedAt = DateTime.UtcNow;
        }

        // 2) карточка
        var assignment = await _db.Assignments.FirstOrDefaultAsync(a => a.TermId == term.Id && a.Direction == direction, ct);
        if (assignment is null)
        {
            assignment = new Assignment { TermId = term.Id, Direction = direction };
            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync(ct);
        }

        // 3) связь пользователь-карточка
        var uaExists = await _db.UserAssignments.AnyAsync(ua => ua.UserLogin == login && ua.AssignmentId == assignment.Id, ct);
        if (!uaExists)
        {
            _db.UserAssignments.Add(new UserAssignment { UserLogin = login, AssignmentId = assignment.Id, IsSolved = false });
        }

        await _db.SaveChangesAsync(ct);

        return new
        {
            termId = term.Id,
            assignmentId = assignment.Id,
            direction = direction.ToString(),
            question,
            translation
        };
    }

    public async Task<IReadOnlyList<object>> GetMyTermsAsync(string login, CancellationToken ct)
    {
        return await _db.UserTerms.Where(x => x.UserLogin == login)
            .OrderByDescending(x => x.LastViewedAt)
            .Select(x => new
            {
                x.TermId,
                x.LastViewedAt,
                En = x.Term!.En,
                Ru = x.Term!.Ru,
                Domain = x.Term!.Domain
            }).ToListAsync(ct);
    }

    private static bool HasCyrillic(string s)
        => s.Any(ch => (ch >= 'А' && ch <= 'я') || ch == 'Ё' || ch == 'ё');
}
