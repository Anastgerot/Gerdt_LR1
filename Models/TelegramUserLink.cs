using System.ComponentModel.DataAnnotations;

namespace Gerdt_LR1.Models;

public class TelegramUserLink
{
    [Key]
    public long TelegramUserId { get; set; }   // ID пользователя Telegram

    public long ChatId { get; set; }           // чат

    [Required]
    public string UserLogin { get; set; } = "";

    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}
