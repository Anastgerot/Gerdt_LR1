using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using static Gerdt_LR1.Controllers.AssignmentsController;

namespace Gerdt_LR1.Services;

public class AssignmentsService : IAssignmentsService
{
    private readonly AppDbContext _db;
    public AssignmentsService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Assignments
            .AsNoTracking()
            .Include(a => a.Term)
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                id = a.Id,
                termId = a.TermId,
                direction = a.Direction.ToString(),
                termEn = a.Term != null ? a.Term.En : null,
                termRu = a.Term != null ? a.Term.Ru : null,
            })
            .ToListAsync<object>(ct);
    }

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
        var a = await _db.Assignments
            .Include(x => x.Term)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (a is null) return null;

        var ua = await _db.UserAssignments
            .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id, ct);

        if (ua is null) return new { forbid = true };

        var term = a.Term!;
        var question = a.Direction == Direction.EnToRu ? term.En : term.Ru;

        // Подбираем набор "альтернатив" в зависимости от направления
        List<Term> relatedTerms;

        if (a.Direction == Direction.EnToRu)
        {
            var key = (term.En ?? "").Trim().ToLower();
            relatedTerms = await _db.Terms
                .Where(t => (t.En ?? "").Trim().ToLower() == key)
                .ToListAsync(ct);
        }
        else // RuToEn
        {
            var key = (term.Ru ?? "").Trim().ToLower();
            relatedTerms = await _db.Terms
                .Where(t => (t.Ru ?? "").Trim().ToLower() == key)
                .ToListAsync(ct);
        }

        // Отдаём варианты при запросе вопроса
        if (dto is null || string.IsNullOrWhiteSpace(dto.Answer))
        {
            var allPossibleTranslations =
                a.Direction == Direction.EnToRu
                    ? relatedTerms.Select(t => t.Ru).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList()
                    : relatedTerms.Select(t => t.En).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

            return new
            {
                assignmentId = a.Id,
                termId = a.TermId,
                direction = a.Direction.ToString(),
                question,
                yourAnswer = (string?)null,
                expected = (string?)null,
                correct = (bool?)null,
                isSolved = ua.IsSolved,
                allPossibleTranslations
            };
        }

        ua.Attempts += 1;
        ua.LastAnsweredAt = DateTime.UtcNow;

        var wasSolved = ua.IsSolved;

        // Проверка ответа по всем возможным вариантам
        bool correct = false;
        Term? matchedTerm = null;

        var normalizedAnswer = dto.Answer.Trim().ToLower();

        if (a.Direction == Direction.EnToRu)
        {
            // EN -> RU: сверяем с Ru
            foreach (var t in relatedTerms)
            {
                var ru = (t.Ru ?? "").Trim();
                if (string.IsNullOrWhiteSpace(ru)) continue;

                var ruLower = ru.ToLower();
                if (ruLower == normalizedAnswer ||
                    ruLower.Split(',').Select(x => x.Trim()).Contains(normalizedAnswer))
                {
                    correct = true;
                    matchedTerm = t;
                    break;
                }
            }
        }
        else
        {
            // RU -> EN: сверяем с En
            foreach (var t in relatedTerms)
            {
                var en = (t.En ?? "").Trim();
                if (string.IsNullOrWhiteSpace(en)) continue;

                var enLower = en.ToLower();
                if (enLower == normalizedAnswer ||
                    enLower.Split(',').Select(x => x.Trim()).Contains(normalizedAnswer))
                {
                    correct = true;
                    matchedTerm = t;
                    break;
                }
            }
        }

        // Зачет и "перекредитование" на другую карточку (в обе стороны)
        if (correct)
        {
            if (matchedTerm != null && matchedTerm.Id != a.TermId)
            {
                // текущую не засчитываем (как у тебя было)
                ua.IsSolved = false;

                var correctAssignment = await _db.Assignments
                    .FirstOrDefaultAsync(x => x.TermId == matchedTerm.Id && x.Direction == a.Direction, ct);

                if (correctAssignment != null)
                {
                    var correctUa = await _db.UserAssignments
                        .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == correctAssignment.Id, ct);

                    if (correctUa != null && !correctUa.IsSolved)
                    {
                        correctUa.IsSolved = true;
                        correctUa.SolvedAt = DateTime.UtcNow;
                        correctUa.Attempts += 1;
                        correctUa.LastAnsweredAt = DateTime.UtcNow;

                        if (!correctUa.ViewedAnswer)
                        {
                            var user = await _db.Users.FindAsync([login], ct);
                            user?.AddPoints(1);
                        }
                    }
                }
            }
            else
            {
                // ответ для текущей карточки
                if (!wasSolved)
                {
                    ua.IsSolved = true;
                    ua.SolvedAt = DateTime.UtcNow;

                    if (!ua.ViewedAnswer)
                    {
                        var user = await _db.Users.FindAsync([login], ct);
                        user?.AddPoints(1);
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        // что показываем как expected
        var expected = a.Direction == Direction.EnToRu ? term.Ru : term.En;

        // все варианты (для UI)
        var allTranslations =
            a.Direction == Direction.EnToRu
                ? relatedTerms.Select(t => t.Ru).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList()
                : relatedTerms.Select(t => t.En).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

        return new
        {
            assignmentId = a.Id,
            termId = a.TermId,
            direction = a.Direction.ToString(),
            question,
            yourAnswer = dto.Answer,
            expected,
            allTranslations,
            correct,
            isSolved = ua.IsSolved,
            creditedToOtherCard = correct && matchedTerm != null && matchedTerm.Id != a.TermId
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
        if (dto.SelectedTermId.HasValue)
        {
            return await CreateByTermIdAsync(login, dto, ct);
        }

        if (string.IsNullOrWhiteSpace(dto.SearchTerm))
            return (new { error = true, msg = "Search term must be provided." }, 400);

        var searchTerm = dto.SearchTerm.Trim().ToLower();
        var dir = dto.Direction ?? Direction.EnToRu;

        List<Term> foundTerms;

        if (dir == Direction.EnToRu)
        {
            // Ищем по английскому термину
            foundTerms = await _db.Terms
                .Where(t => t.En.ToLower().Contains(searchTerm))
                .ToListAsync(ct);
        }
        else 
        {
            // Ищем по русскому термину
            foundTerms = await _db.Terms
                .Where(t => t.Ru.ToLower().Contains(searchTerm))
                .ToListAsync(ct);
        }

        if (!foundTerms.Any())
        {
            return (new
            {
                notFound = true,
                msg = $"No terms found for '{dto.SearchTerm}' in direction {dir}.",
                searchTerm = dto.SearchTerm,
                direction = dir.ToString()
            }, 404);
        }

        // Если нашли только один вариант, создаем карточку
        if (foundTerms.Count == 1)
        {
            return await CreateAssignmentForTerm(login, foundTerms[0].Id, dir, ct);
        }

        // Если нашли несколько вариантов, возвращаем список для выбора
        return (new
        {
            multipleChoices = true,
            msg = $"Found {foundTerms.Count} terms matching '{dto.SearchTerm}'",
            terms = foundTerms.Select(t => new
            {
                termId = t.Id,
                en = t.En,
                ru = t.Ru
            }),
            searchTerm = dto.SearchTerm,
            direction = dir.ToString()
        }, 200); 
    }

    // Вспомогательный метод для создания карточки по ID термина
    private async Task<(object result, int createdStatus)> CreateByTermIdAsync(string login, AssignForMeDto dto, CancellationToken ct)
    {
        var term = await _db.Terms.FindAsync([dto.SelectedTermId!.Value], ct);
        if (term is null)
            return (new { notFound = true, msg = $"Term with id={dto.SelectedTermId} not found." }, 404);

        var dir = dto.Direction ?? Direction.EnToRu;
        return await CreateAssignmentForTerm(login, term.Id, dir, ct);
    }

    // Вспомогательный метод для создания Assignment для термина
    private async Task<(object result, int createdStatus)> CreateAssignmentForTerm(string login, int termId, Direction direction, CancellationToken ct)
    {
        // Сначала проверяем, нет ли у пользователя уже этой карточки
        var existingUserAssignment = await _db.UserAssignments
            .Include(ua => ua.Assignment)
            .FirstOrDefaultAsync(ua =>
                ua.UserLogin == login &&
                ua.Assignment.TermId == termId &&
                ua.Assignment.Direction == direction, ct);

        if (existingUserAssignment != null)
            return (new { conflict = true, msg = "This assignment is already linked to the current user." }, 409);

        // Ищем Assignment
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.TermId == termId && a.Direction == direction, ct);

        if (assignment is null)
        {
            assignment = new Assignment { TermId = termId, Direction = direction };
            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync(ct);
        }

        // Создаем связь с пользователем
        _db.UserAssignments.Add(new UserAssignment
        {
            UserLogin = login,
            AssignmentId = assignment.Id,
            IsSolved = false
        });
        await _db.SaveChangesAsync(ct);

        var term = await _db.Terms.FindAsync([termId], ct);
        var question = direction == Direction.EnToRu ? term!.En : term!.Ru;

        return (new
        {
            success = true,
            assignmentId = assignment.Id,
            termId = term!.Id,
            direction = direction.ToString(),
            question,
            term = new { term.En, term.Ru }
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

        var ua = await _db.UserAssignments.FirstOrDefaultAsync(
            x => x.UserLogin == login && x.AssignmentId == a.Id, ct);

        if (ua is null)
            return new { forbid = true };
        ua.IsSolved = false;
        ua.SolvedAt = null;
        ua.ViewedAnswer = false; 

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
            lastAnsweredAt = ua.LastAnsweredAt,
            viewedAnswer = ua.ViewedAnswer 
        };
    }



    public async Task<List<SolvedAssignmentDto>> GetSolvedAsync(string login, CancellationToken ct)
    {
        var qBase = _db.UserAssignments
            .Where(x => x.UserLogin == login && x.IsSolved)
            .Include(x => x.Assignment)!
                .ThenInclude(a => a.Term);

        return await qBase
            .OrderByDescending(x => x.LastAnsweredAt ?? x.SolvedAt ?? DateTime.MinValue)
            .Select(x => new SolvedAssignmentDto
            {
                AssignmentId = x.AssignmentId,
                TermId = x.Assignment!.TermId,

                Direction = x.Assignment.Direction.ToString(), 
      
                Question = x.Assignment.Direction == Direction.EnToRu
                    ? x.Assignment.Term!.En
                    : x.Assignment.Term!.Ru,

                Expected = x.Assignment.Term!.Translate(x.Assignment.Direction), 

                Attempts = x.Attempts
            })
            .ToListAsync(ct);
    }

    public async Task<bool> PeekAnswerAsync(int assignmentId, string login, CancellationToken ct)
    {
        var ua = await _db.UserAssignments
            .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == assignmentId, ct);

        if (ua == null) return false;

        ua.ViewedAnswer = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }


}
