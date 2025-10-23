using Gerdt_LR1.Models;

namespace Gerdt_LR1.Services;

public record TranslateDto(string Text, Direction? Direction);

public interface ITermsService
{
    Task<IReadOnlyList<Term>> GetAllAsync(CancellationToken ct);
    Task<Term?> GetByIdAsync(int id, CancellationToken ct);
    Task<(bool ok, string? conflictMsg)> UpdateAsync(int id, Term input, CancellationToken ct);
    Task<(Term? created, string? conflictMsg)> CreateAsync(Term input, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);

    Task<object?> TranslateAndRememberAsync(string login, TranslateDto dto, CancellationToken ct);

    Task<IReadOnlyList<object>> GetMyTermsAsync(string login, CancellationToken ct);
}
