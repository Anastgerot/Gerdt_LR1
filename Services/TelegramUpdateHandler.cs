using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Gerdt_LR1.Services;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TelegramUpdateHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Telegram.Bot.Types.Update update, CancellationToken ct)
    {
        if (update.Type != UpdateType.Message) return;
        var msg = update.Message;
        if (msg?.Text is null) return;

        if (msg.Chat.Type != ChatType.Private)
        {
            await botClient.SendMessage(msg.Chat.Id, "Пожалуйста, используйте личный чат с ботом.", cancellationToken: ct);
            return;
        }

        var chatId = msg.Chat.Id;
        var tgUserId = msg.From?.Id ?? 0;
        var text = msg.Text.Trim();


        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(chatId,
                "Здравствуйте. Это бот Gerdt.\n\n" +
                "Доступные команды:\n" +
                "/signin — вход\n" +
                "/signup — регистрация\n" +
                "/logout — выход\n" +
                "/me — сведения об учетной записи\n",
                cancellationToken: ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // state timeout (10 минут)
        var now = DateTime.UtcNow;
        var state = await db.TelegramAuthStates.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
        if (state != null && state.UpdatedAtUtc < now.AddMinutes(-10))
        {
            db.TelegramAuthStates.Remove(state);
            await db.SaveChangesAsync(ct);
            state = null;
        }

        if (text.Equals("/logout", StringComparison.OrdinalIgnoreCase))
        {
            var link = await db.TelegramUserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (link != null) db.TelegramUserLinks.Remove(link);

            if (state != null) db.TelegramAuthStates.Remove(state);

            await db.SaveChangesAsync(ct);
            await botClient.SendMessage(chatId, "Сеанс завершен. Для продолжения используйте /signin или /signup.", cancellationToken: ct);
            return;
        }

        // /me
        if (text.Equals("/me", StringComparison.OrdinalIgnoreCase))
        {
            var login = await db.TelegramUserLinks
                .Where(x => x.TelegramUserId == tgUserId)
                .Select(x => x.UserLogin)
                .FirstOrDefaultAsync(ct);

            if (login is null)
            {
                await botClient.SendMessage(chatId,
                    "Авторизация не выполнена. Для входа используйте /signin, для регистрации — /signup.",
                    cancellationToken: ct);
                return;
            }

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Login == login, ct);
            if (user is null)
            {
                await botClient.SendMessage(chatId,
                    "Привязка учетной записи найдена, но пользователь в системе не обнаружен.",
                    cancellationToken: ct);
                return;
            }

            var qBase = db.UserAssignments.Where(x => x.UserLogin == login);

            var total = await qBase.CountAsync(ct);
            var solved = await qBase.CountAsync(x => x.IsSolved, ct);
            var unsolved = total - solved;

            var attemptsTotal = await qBase.SumAsync(x => (int?)x.Attempts, ct) ?? 0;
            var lastSolvedAt = await qBase.MaxAsync(x => (DateTime?)x.SolvedAt, ct);
            var lastAnsweredAt = await qBase.MaxAsync(x => (DateTime?)x.LastAnsweredAt, ct);

            var byDomain = await qBase
                .GroupBy(x => x.Assignment!.Term!.Domain.ToString())
                .Select(g => new
                {
                    domain = g.Key,
                    solved = g.Count(x => x.IsSolved),
                    unsolved = g.Count(x => !x.IsSolved),
                })
                .OrderByDescending(x => x.solved)
                .ToListAsync(ct);

            var hardestUnsolved = await qBase
                .Where(x => !x.IsSolved && x.Attempts > 0)
                .OrderByDescending(x => x.Attempts)
                .Take(5)
                .Select(x => new
                {
                    question = x.Assignment!.Direction == Direction.EnToRu
                        ? x.Assignment.Term!.En
                        : x.Assignment.Term!.Ru,
                    attempts = x.Attempts
                })
                .ToListAsync(ct);

            var mostAttemptsSolved = await qBase
                .Where(x => x.IsSolved)
                .OrderByDescending(x => x.Attempts)
                .Take(5)
                .Select(x => new
                {
                    question = x.Assignment!.Direction == Direction.EnToRu
                        ? x.Assignment.Term!.En
                        : x.Assignment.Term!.Ru,
                    attempts = x.Attempts,
                    solvedAt = x.SolvedAt
                })
                .ToListAsync(ct);

            static string FmtDt(DateTime? dt)
            {
                if (dt is null) return "—";
                return dt.Value.ToString("dd.MM.yyyy HH:mm");
            }

            string Percent(int part, int all)
            {
                if (all <= 0) return "0%";
                var p = (int)Math.Round((double)part * 100.0 / all);
                return $"{p}%";
            }

            var header =
                $"Логин: *{login}*\n" +
                $"Баллы: *{user.Points}*\n";

            var progressLine =
                $"Карточки: *{solved}/{total}* ({Percent(solved, total)})\n" +
                $"Попытки: *{attemptsTotal}*\n" +
                $"Последнее решение: *{FmtDt(lastSolvedAt)}*\n" +
                $"Последний ответ: *{FmtDt(lastAnsweredAt)}*\n";

            var domainsLine = "";
            if (byDomain.Count > 0)
            {
                var topDomains = byDomain.Take(5) 
                    .Select(d => $"  •   {d.domain}: {d.solved}/{d.solved + d.unsolved}")
                    .ToList();

                domainsLine = "\nДомен(ы):\n" + string.Join("\n", topDomains);

                if (byDomain.Count > 5)
                    domainsLine += "\n…";

                domainsLine += "\n";
            }

            string BuildTopList<T>(string title, List<T> items, Func<T, string> fmt)
            {
                if (items.Count == 0) return "";

                var lines = items
                    .Take(5)
                    .Select((x, i) => $" {i + 1})  {fmt(x)}")
                    .ToList();

                return $"\n{title}\n{string.Join("\n", lines)}\n";
            }


            var hardestBlock = BuildTopList(
                "Сложнее всего (нерешенные):",
                hardestUnsolved,
                x => $"{x.question} — попытки: {x.attempts}"
            );

            var solvedHardBlock = BuildTopList(
                "Решенные с наибольшим числом попыток:",
                mostAttemptsSolved,
                x => $"{x.question} — попытки: {x.attempts}, решено: {FmtDt(x.solvedAt)}"
            );

            var msgText = header + "\n" + progressLine + domainsLine + hardestBlock + solvedHardBlock;

            await botClient.SendMessage(chatId, msgText, parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;

        }

        if (text.Equals("/signin", StringComparison.OrdinalIgnoreCase))
        {
            var already = await db.TelegramUserLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (already != null)
            {
                await botClient.SendMessage(chatId,
                    $"Авторизация уже выполнена для учетной записи: {already.UserLogin}.\n" +
                    "Для завершения сеанса используйте /logout.",
                    cancellationToken: ct);
                return;
            }

            if (state != null) db.TelegramAuthStates.Remove(state);

            db.TelegramAuthStates.Add(new TelegramAuthState
            {
                TelegramUserId = tgUserId,
                Step = "await_login",
                UpdatedAtUtc = now
            });
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите логин:", cancellationToken: ct);
            return;
        }

        if (text.Equals("/signup", StringComparison.OrdinalIgnoreCase))
        {
            var already = await db.TelegramUserLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (already != null)
            {
                await botClient.SendMessage(chatId,
                    $"Авторизация уже выполнена для учетной записи: {already.UserLogin}.\n" +
                    "Для завершения сеанса используйте /logout.",
                    cancellationToken: ct);
                return;
            }

            if (state != null) db.TelegramAuthStates.Remove(state);

            db.TelegramAuthStates.Add(new TelegramAuthState
            {
                TelegramUserId = tgUserId,
                Step = "await_reg_login",
                UpdatedAtUtc = now
            });
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите желаемый логин:", cancellationToken: ct);
            return;
        }

        if (state?.Step == "await_login")
        {
            state.TempLogin = text;
            state.Step = "await_password";
            state.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите пароль:", cancellationToken: ct);
            return;
        }

        if (state?.Step == "await_password" && !string.IsNullOrWhiteSpace(state.TempLogin))
        {
            var login = state.TempLogin.Trim();
            var password = text;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
            if (user is null || !user.CheckPassword(password))
            {
                db.TelegramAuthStates.Remove(state);
                await db.SaveChangesAsync(ct);

                await botClient.SendMessage(chatId, "Ошибка авторизации: неверный логин или пароль. Повторите попытку командой /signin.", cancellationToken: ct);
                return;
            }

            db.TelegramUserLinks.Add(new TelegramUserLink
            {
                TelegramUserId = tgUserId,
                ChatId = chatId,
                UserLogin = login,
                LinkedAtUtc = now
            });

            db.TelegramAuthStates.Remove(state);
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, $"Авторизация выполнена. Учетная запись: {login}.\nДля просмотра сведений используйте /me.", cancellationToken: ct);
            return;
        }

        if (state?.Step == "await_reg_login")
        {
            var login = text.Trim();

            if (login.Length < 3)
            {
                await botClient.SendMessage(chatId, "Ошибка: логин слишком короткий. Введите другой логин:", cancellationToken: ct);
                return;
            }
            if (login.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.SendMessage(chatId, "Ошибка: логин admin зарезервирован. Введите другой логин:", cancellationToken: ct);
                return;
            }

            var exists = await db.Users.AnyAsync(u => u.Login == login, ct);
            if (exists)
            {
                await botClient.SendMessage(chatId, "Ошибка: такой логин уже существует. Введите другой логин:", cancellationToken: ct);
                return;
            }

            state.TempLogin = login;
            state.Step = "await_reg_password";
            state.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите пароль для регистрации:", cancellationToken: ct);
            return;
        }

        if (state?.Step == "await_reg_password" && !string.IsNullOrWhiteSpace(state.TempLogin))
        {
            var login = state.TempLogin.Trim();
            var password = text;

            if (password.Length < 4)
            {
                await botClient.SendMessage(chatId, "Ошибка: пароль слишком короткий. Введите другой пароль:", cancellationToken: ct);
                return;
            }

            var newUser = new User { Login = login };
            newUser.SetPassword(password);

            db.Users.Add(newUser);
            db.TelegramUserLinks.Add(new TelegramUserLink
            {
                TelegramUserId = tgUserId,
                ChatId = chatId,
                UserLogin = login,
                LinkedAtUtc = now
            });

            db.TelegramAuthStates.Remove(state);
            await db.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, $"Регистрация завершена. Учетная запись: {login}.\nДля просмотра сведений используйте /me.", cancellationToken: ct);
            return;
        }

        var linked = await db.TelegramUserLinks.AnyAsync(x => x.TelegramUserId == tgUserId, ct);
        if (!linked)
        {
            await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin или /signup.", cancellationToken: ct);
            return;
        }

        await botClient.SendMessage(chatId, "Команда не распознана. Для просмотра доступных команд используйте /start.", cancellationToken: ct);
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        var msg = exception is ApiRequestException apiEx
            ? $"Telegram API error [{apiEx.ErrorCode}] ({source}): {apiEx.Message}"
            : $"Telegram error ({source}): {exception}";
        Console.WriteLine(msg);
        return Task.CompletedTask;
    }
}
