using Gerdt_LR1.Models;

namespace Gerdt_LR1.Services;

public record AnswerDto(string? Answer);
public class AssignForMeDto
{
    public Direction? Direction { get; set; }
    public string? SearchTerm { get; set; }
    public int? SelectedTermId { get; set; }
}

public record GenerateAssignmentsDto(int Count, Direction Direction);
public record AddAssignmentsDto(int Count, string UserLogin);
public record ResetAssignmentDto(bool ResetAttempts = true, bool ClearTimestamps = true);

public class SolvedAssignmentDto
{
    public int AssignmentId { get; set; }
    public int TermId { get; set; }
    public string Question { get; set; } = "";
    public int Attempts { get; set; }

    public string Direction { get; set; } = "";  
    public string? Expected { get; set; }         
}

public interface IAssignmentsService
{
    Task<IReadOnlyList<object>> GetAllAsync(CancellationToken ct);
    Task<Assignment?> GetByIdAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<object>> GetUserAssignmentsAsync(string login, bool? solved, CancellationToken ct);

    Task<bool> DeleteAsync(int id, CancellationToken ct);

    Task<object?> GetQuestionOrCheckAnswerAsync(int id, string login, AnswerDto? dto, CancellationToken ct);
    Task<bool> IsLinkedAsync(int assignmentId, string login, CancellationToken ct);

    Task<object> SwitchDirectionAsync(int id, string login, CancellationToken ct);

    Task<(object result, int createdStatus)> CreateForUserAsync(string login, AssignForMeDto dto, CancellationToken ct);

    Task<(IEnumerable<object> items, int requested, int created)> GenerateAsync(GenerateAssignmentsDto dto, CancellationToken ct);

    Task<(IEnumerable<object> items, int createdLinks)> AddAssignmentsToUserAsync(AddAssignmentsDto dto, CancellationToken ct);

    Task<object?> MarkUnsolvedAsync(int id, string login, ResetAssignmentDto? dto, CancellationToken ct);

    Task<List<SolvedAssignmentDto>> GetSolvedAsync(string login, CancellationToken ct);

    Task<bool> PeekAnswerAsync(int assignmentId, string login, CancellationToken ct);

    }
