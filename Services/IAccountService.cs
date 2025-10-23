using Gerdt_LR1.Models;

namespace Gerdt_LR1.Services;

public record RegisterDto(string Login, string Password);
public record LoginData(string login, string password);

public interface IAccountService
{
    Task<(User? created, string? error)> RegisterAsync(RegisterDto dto, CancellationToken ct);
    Task<object?> GetTokenAsync(LoginData ld, CancellationToken ct);
    Task<object?> MyStatsAsync(string login, CancellationToken ct);
}
