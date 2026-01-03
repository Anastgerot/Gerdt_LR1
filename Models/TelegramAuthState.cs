using System.ComponentModel.DataAnnotations;

namespace Gerdt_LR1.Models;

public class TelegramAuthState
{
    [Key]
    public long TelegramUserId { get; set; }

    public string Step { get; set; } = "";

    public string? TempLogin { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
