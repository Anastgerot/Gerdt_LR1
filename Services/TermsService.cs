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

        // Находим ВСЕ термины, которые содержат этот текст как английский или русский вариант
        var terms = await _db.Terms
            .Where(t => t.En.ToLower() == textLower || t.Ru.ToLower() == textLower)
            .ToListAsync(ct);

        if (!terms.Any()) return null;

        var direction = dto.Direction ?? (HasCyrillic(text) ? Direction.RuToEn : Direction.EnToRu);

        // Создаем списки всех возможных вопросов и переводов
        var translations = new List<object>();

        foreach (var term in terms)
        {
            var question = direction == Direction.EnToRu ? term.En : term.Ru;
            var translation = direction == Direction.EnToRu ? term.Ru : term.En;

            // 1) история просмотров пользователя для каждого термина
            var link = await _db.UserTerms.FirstOrDefaultAsync(x => x.UserLogin == login && x.TermId == term.Id, ct);
            if (link is null)
            {
                _db.UserTerms.Add(new UserTerm { UserLogin = login, TermId = term.Id, LastViewedAt = DateTime.UtcNow });
            }
            else
            {
                link.LastViewedAt = DateTime.UtcNow;
            }

            // 2) карточка для каждого термина
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

            translations.Add(new
            {
                termId = term.Id,
                assignmentId = assignment.Id,
                direction = direction.ToString(),
                question,
                translation,
                allTranslations = GetOppositeTranslations(terms, direction, textLower)
            });
        }

        await _db.SaveChangesAsync(ct);

        // Если нашли только один термин, возвращаем его
        if (translations.Count == 1)
        {
            return translations[0];
        }

        // Если нашли несколько терминов, возвращаем все
        return new
        {
            multipleTranslations = true,
            count = translations.Count,
            translations,
            // Для обратной совместимости - первый перевод
            termId = terms[0].Id,
            direction = direction.ToString(),
            question = direction == Direction.EnToRu ? terms[0].En : terms[0].Ru,
            translation = direction == Direction.EnToRu ? terms[0].Ru : terms[0].En,
            allTranslations = GetOppositeTranslations(terms, direction, textLower)
        };
    }

    // Вспомогательный метод для получения всех переводов в противоположном направлении
    private List<string> GetOppositeTranslations(List<Term> terms, Direction direction, string originalTextLower)
    {
        var translations = new List<string>();

        foreach (var term in terms)
        {
            if (direction == Direction.EnToRu)
            {
                // Если исходный текст на английском, собираем все русские переводы
                if (term.En.ToLower() == originalTextLower && !string.IsNullOrWhiteSpace(term.Ru))
                {
                    translations.Add(term.Ru);
                }
            }
            else
            {
                // Если исходный текст на русском, собираем все английские переводы
                if (term.Ru.ToLower() == originalTextLower && !string.IsNullOrWhiteSpace(term.En))
                {
                    translations.Add(term.En);
                }
            }
        }

        return translations.Distinct().ToList();
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
