using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Gerdt_LR1.Services;

public sealed class TelegramBotHostedService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly TelegramUpdateHandler _handler;

    public TelegramBotHostedService(ITelegramBotClient bot, TelegramUpdateHandler handler)
    {
        _bot = bot;
        _handler = handler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _bot.StartReceiving(
            updateHandler: _handler,
            receiverOptions: options,
            cancellationToken: stoppingToken
        );

        Console.WriteLine("Telegram bot started (polling).");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
