using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;

namespace Gerdt_LR1.Services;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Type != UpdateType.Message) return;

        var msg = update.Message;
        if (msg?.Text is null) return;

        var chatId = msg.Chat.Id;
        var text = msg.Text.Trim();

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: "Привет! Я бот тренажера.\n\nКоманды:\n/start — помощь\n/ping — проверка связи",
                cancellationToken: ct
            );
            return;
        }

        if (text.StartsWith("/ping", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(chatId, "pong", cancellationToken: ct);
            return;
        }

        await botClient.SendMessage(chatId, "Пока понимаю только /start и /ping", cancellationToken: ct);
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken ct)
    {
        var msg = exception is ApiRequestException apiEx
            ? $"Telegram API error [{apiEx.ErrorCode}] ({source}): {apiEx.Message}"
            : $"Telegram error ({source}): {exception}";

        Console.WriteLine(msg);
        return Task.CompletedTask;
    }
}
