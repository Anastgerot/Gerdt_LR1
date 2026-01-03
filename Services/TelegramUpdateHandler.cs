using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

using AppUser = Gerdt_LR1.Models.User; // <-- FIX: убираем неоднозначность User

namespace Gerdt_LR1.Services;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TelegramUpdateHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private sealed class BotSession
    {
        public string Mode { get; set; } = "menu";            // menu | translate | trainer
        public Direction? TranslateDirection { get; set; }    // EnToRu / RuToEn

        public string Step { get; set; } = "";                // await_translate_text | await_trainer_answer | trainer_choose
        public int? CurrentAssignmentId { get; set; }         // текущая карточка в тренажере

        public string TrainerMode { get; set; } = "";         // solve | (после reset тоже solve)
    }

    private static readonly ConcurrentDictionary<long, BotSession> Sessions = new();
    private static BotSession GetSession(long tgUserId) => Sessions.GetOrAdd(tgUserId, _ => new BotSession());

    private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        // ====== CALLBACK кнопки ======
        if (update.Type == UpdateType.CallbackQuery)
        {
            var cq = update.CallbackQuery;
            if (cq?.Data is null || cq.Message is null) return;

            await botClient.AnswerCallbackQuery(cq.Id, cancellationToken: ct);

            if (cq.Message.Chat.Type != ChatType.Private)
            {
                await botClient.SendMessage(cq.Message.Chat.Id, "Пожалуйста, используйте личный чат с ботом.", cancellationToken: ct);
                return;
            }

            var chatIdCb = cq.Message.Chat.Id;
            var tgUserIdCb = cq.From.Id;
            var s = GetSession(tgUserIdCb);

            switch (cq.Data)
            {
                case "menu_translate":
                    s.Mode = "translate";
                    s.Step = "";
                    s.CurrentAssignmentId = null;
                    s.TrainerMode = "";
                    await SendTranslateMenu(botClient, chatIdCb, s.TranslateDirection, ct);
                    return;

                case "tr_dir_enru":
                    s.Mode = "translate";
                    s.TranslateDirection = Direction.EnToRu;
                    s.Step = "";
                    await botClient.SendMessage(chatIdCb, "Выбрано направление: EN → RU.\nКоманда: /translate", cancellationToken: ct);
                    return;

                case "tr_dir_ruen":
                    s.Mode = "translate";
                    s.TranslateDirection = Direction.RuToEn;
                    s.Step = "";
                    await botClient.SendMessage(chatIdCb, "Выбрано направление: RU → EN.\nКоманда: /translate", cancellationToken: ct);
                    return;

                case "menu_trainer":
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var login = await db.TelegramUserLinks
                            .Where(x => x.TelegramUserId == tgUserIdCb)
                            .Select(x => x.UserLogin)
                            .FirstOrDefaultAsync(ct);

                        if (login is null)
                        {
                            await botClient.SendMessage(chatIdCb, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                            return;
                        }

                        s.Mode = "trainer";
                        s.Step = "trainer_choose";
                        s.CurrentAssignmentId = null;
                        s.TrainerMode = "";

                        await SendTrainerChooseMenu(botClient, chatIdCb, ct);
                        return;
                    }

                case "trainer_choose":
                    s.Mode = "trainer";
                    s.Step = "trainer_choose";
                    s.CurrentAssignmentId = null;
                    s.TrainerMode = "";
                    await SendTrainerChooseMenu(botClient, chatIdCb, ct);
                    return;

                case "trainer_solve":
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var assignmentsService = scope.ServiceProvider.GetRequiredService<IAssignmentsService>();

                        var login = await db.TelegramUserLinks
                            .Where(x => x.TelegramUserId == tgUserIdCb)
                            .Select(x => x.UserLogin)
                            .FirstOrDefaultAsync(ct);

                        if (login is null)
                        {
                            await botClient.SendMessage(chatIdCb, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                            return;
                        }

                        s.Mode = "trainer";
                        s.TrainerMode = "solve";
                        s.Step = "";
                        s.CurrentAssignmentId = null;

                        await botClient.SendMessage(chatIdCb, "Ок, решаем нерешённые карточки", cancellationToken: ct);
                        await SendNextTrainerQuestionAsync(botClient, db, assignmentsService, chatIdCb, login, s, ct);
                        return;
                    }

                case "trainer_resume":
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var assignmentsService = scope.ServiceProvider.GetRequiredService<IAssignmentsService>();

                        var login = await db.TelegramUserLinks
                            .Where(x => x.TelegramUserId == tgUserIdCb)
                            .Select(x => x.UserLogin)
                            .FirstOrDefaultAsync(ct);

                        if (login is null)
                        {
                            await botClient.SendMessage(chatIdCb, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                            return;
                        }

                        var solvedIds = await db.UserAssignments
                            .Where(ua => ua.UserLogin == login && ua.IsSolved)
                            .Select(ua => ua.AssignmentId)
                            .ToListAsync(ct);

                        if (solvedIds.Count == 0)
                        {
                            await botClient.SendMessage(chatIdCb, "У вас нет решённых карточек для возобновления. Начинаю решать нерешённые", cancellationToken: ct);

                            s.Mode = "trainer";
                            s.TrainerMode = "solve";
                            s.Step = "";
                            s.CurrentAssignmentId = null;

                            await SendNextTrainerQuestionAsync(botClient, db, assignmentsService, chatIdCb, login, s, ct);
                            return;
                        }

                        int resetCount = 0;
                        foreach (var id in solvedIds)
                        {
                            var res = await assignmentsService.MarkUnsolvedAsync(id, login, new ResetAssignmentDto(), ct);
                            if (res is not null) resetCount++;
                        }

                        s.Mode = "trainer";
                        s.TrainerMode = "solve";
                        s.Step = "";
                        s.CurrentAssignmentId = null;

                        await botClient.SendMessage(chatIdCb, $"Возобновлено карточек: {resetCount}. Начинаем заново", cancellationToken: ct);
                        await SendNextTrainerQuestionAsync(botClient, db, assignmentsService, chatIdCb, login, s, ct);
                        return;
                    }

                case "trainer_next":
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var assignmentsService = scope.ServiceProvider.GetRequiredService<IAssignmentsService>();

                        var login = await db.TelegramUserLinks
                            .Where(x => x.TelegramUserId == tgUserIdCb)
                            .Select(x => x.UserLogin)
                            .FirstOrDefaultAsync(ct);

                        if (login is null)
                        {
                            await botClient.SendMessage(chatIdCb, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                            return;
                        }

                        if (s.CurrentAssignmentId is not null)
                            await SkipCurrentAsync(db, login, s.CurrentAssignmentId.Value, ct);

                        s.Mode = "trainer";
                        if (string.IsNullOrWhiteSpace(s.TrainerMode))
                            s.TrainerMode = "solve";

                        s.Step = "";
                        s.CurrentAssignmentId = null;

                        await SendNextTrainerQuestionAsync(botClient, db, assignmentsService, chatIdCb, login, s, ct);
                        return;
                    }

                case "menu_back":
                    s.Mode = "menu";
                    s.Step = "";
                    s.CurrentAssignmentId = null;
                    s.TrainerMode = "";
                    await SendMainMenu(botClient, chatIdCb, ct);
                    return;

                default:
                    await botClient.SendMessage(chatIdCb, "Действие не распознано. Используйте /menu.", cancellationToken: ct);
                    return;
            }
        }

        // ====== MESSAGE ======
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
        if (tgUserId == 0) return;

        var text = msg.Text.Trim();
        var session = GetSession(tgUserId);

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(chatId,
                "Здравствуйте. Это бот Gerdt.\n\n" +
                "Доступные команды:\n" +
                "/menu — меню\n" +
                "/signin — вход\n" +
                "/signup — регистрация\n" +
                "/logout — выход\n" +
                "/me — сведения об учетной записи\n" +
                "/translate — перевод (в режиме «Перевод»)\n" +
                "В режиме «Тренажёр» просто вводите ответ на вопрос.\n" +
                "/next — следующий вопрос (в тренажёре)\n" +
                "/trainer — открыть выбор тренажёра\n",
                cancellationToken: ct);
            return;
        }

        using var scopeMsg = _scopeFactory.CreateScope();
        var dbMsg = scopeMsg.ServiceProvider.GetRequiredService<AppDbContext>();
        var termsService = scopeMsg.ServiceProvider.GetRequiredService<ITermsService>();
        var assignmentsServiceMsg = scopeMsg.ServiceProvider.GetRequiredService<IAssignmentsService>();

        var now = DateTime.UtcNow;
        var state = await dbMsg.TelegramAuthStates.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
        if (state != null && state.UpdatedAtUtc < now.AddMinutes(-10))
        {
            dbMsg.TelegramAuthStates.Remove(state);
            await dbMsg.SaveChangesAsync(ct);
            state = null;
        }

        if (text.Equals("/logout", StringComparison.OrdinalIgnoreCase))
        {
            var link = await dbMsg.TelegramUserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (link != null) dbMsg.TelegramUserLinks.Remove(link);
            if (state != null) dbMsg.TelegramAuthStates.Remove(state);

            await dbMsg.SaveChangesAsync(ct);

            session.Mode = "menu";
            session.Step = "";
            session.TranslateDirection = null;
            session.CurrentAssignmentId = null;
            session.TrainerMode = "";

            await botClient.SendMessage(chatId, "Сеанс завершен. Для продолжения используйте /signin или /signup.", cancellationToken: ct);
            return;
        }

        // /signin
        if (text.Equals("/signin", StringComparison.OrdinalIgnoreCase))
        {
            var already = await dbMsg.TelegramUserLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);

            if (already != null)
            {
                await botClient.SendMessage(chatId,
                    $"Авторизация уже выполнена для учетной записи: {already.UserLogin}.\n" +
                    "Для завершения сеанса используйте /logout.",
                    cancellationToken: ct);
                return;
            }

            if (state != null) dbMsg.TelegramAuthStates.Remove(state);

            dbMsg.TelegramAuthStates.Add(new TelegramAuthState
            {
                TelegramUserId = tgUserId,
                Step = "await_login",
                UpdatedAtUtc = now
            });
            await dbMsg.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите логин:", cancellationToken: ct);
            return;
        }

        // /signup
        if (text.Equals("/signup", StringComparison.OrdinalIgnoreCase))
        {
            var already = await dbMsg.TelegramUserLinks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);

            if (already != null)
            {
                await botClient.SendMessage(chatId,
                    $"Авторизация уже выполнена для учетной записи: {already.UserLogin}.\n" +
                    "Для завершения сеанса используйте /logout.",
                    cancellationToken: ct);
                return;
            }

            if (state != null) dbMsg.TelegramAuthStates.Remove(state);

            dbMsg.TelegramAuthStates.Add(new TelegramAuthState
            {
                TelegramUserId = tgUserId,
                Step = "await_reg_login",
                UpdatedAtUtc = now
            });
            await dbMsg.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите желаемый логин:", cancellationToken: ct);
            return;
        }

        // шаги логина/регистрации
        if (state?.Step == "await_login")
        {
            state.TempLogin = text;
            state.Step = "await_password";
            state.UpdatedAtUtc = now;
            await dbMsg.SaveChangesAsync(ct);

            await botClient.SendMessage(chatId, "Введите пароль:", cancellationToken: ct);
            return;
        }

        if (state?.Step == "await_password" && !string.IsNullOrWhiteSpace(state.TempLogin))
        {
            var login = state.TempLogin.Trim();
            var password = text;

            var user = await dbMsg.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
            if (user is null || !user.CheckPassword(password))
            {
                dbMsg.TelegramAuthStates.Remove(state);
                await dbMsg.SaveChangesAsync(ct);

                await botClient.SendMessage(chatId,
                    "Ошибка авторизации: неверный логин или пароль. Повторите попытку командой /signin.",
                    cancellationToken: ct);
                return;
            }

            var existingLink = await dbMsg.TelegramUserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (existingLink != null) dbMsg.TelegramUserLinks.Remove(existingLink);

            dbMsg.TelegramUserLinks.Add(new TelegramUserLink
            {
                TelegramUserId = tgUserId,
                ChatId = chatId,
                UserLogin = login,
                LinkedAtUtc = now
            });

            dbMsg.TelegramAuthStates.Remove(state);
            await dbMsg.SaveChangesAsync(ct);

            session.Mode = "menu";
            session.Step = "";
            session.CurrentAssignmentId = null;
            session.TrainerMode = "";

            await botClient.SendMessage(chatId,
                $"Авторизация выполнена. Учетная запись: {login}.\nОткройте /menu для выбора режима.",
                cancellationToken: ct);
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

            var exists = await dbMsg.Users.AnyAsync(u => u.Login == login, ct);
            if (exists)
            {
                await botClient.SendMessage(chatId, "Ошибка: такой логин уже существует. Введите другой логин:", cancellationToken: ct);
                return;
            }

            state.TempLogin = login;
            state.Step = "await_reg_password";
            state.UpdatedAtUtc = now;
            await dbMsg.SaveChangesAsync(ct);

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

            // FIX: используем AppUser (alias), чтобы не конфликтовало с Telegram.Bot.Types.User
            var newUser = new AppUser { Login = login };
            newUser.SetPassword(password);
            dbMsg.Users.Add(newUser);

            var existingLink = await dbMsg.TelegramUserLinks.FirstOrDefaultAsync(x => x.TelegramUserId == tgUserId, ct);
            if (existingLink != null) dbMsg.TelegramUserLinks.Remove(existingLink);

            dbMsg.TelegramUserLinks.Add(new TelegramUserLink
            {
                TelegramUserId = tgUserId,
                ChatId = chatId,
                UserLogin = login,
                LinkedAtUtc = now
            });

            dbMsg.TelegramAuthStates.Remove(state);
            await dbMsg.SaveChangesAsync(ct);

            session.Mode = "menu";
            session.Step = "";
            session.CurrentAssignmentId = null;
            session.TrainerMode = "";

            await botClient.SendMessage(chatId,
                $"Регистрация завершена. Учетная запись: {login}.\nОткройте /menu для выбора режима.",
                cancellationToken: ct);
            return;
        }


        // /me
        if (text.Equals("/me", StringComparison.OrdinalIgnoreCase))
        {
            var login = await dbMsg.TelegramUserLinks
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

            var user = await dbMsg.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Login == login, ct);
            if (user is null)
            {
                await botClient.SendMessage(chatId,
                    "Привязка учетной записи найдена, но пользователь в системе не обнаружен.",
                    cancellationToken: ct);
                return;
            }

            var qBase = dbMsg.UserAssignments.Where(x => x.UserLogin == login);

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

            static string Percent(int part, int all)
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
                var topDomains = byDomain
                    .Take(5)
                    .Select(d => $" •   {d.domain}: решено {d.solved}, не решено {d.unsolved}")
                    .ToList();

                domainsLine = "\nДомен(ы):\n" + string.Join("\n", topDomains) + "\n";
            }

            static string BuildTopList<T>(string title, List<T> items, Func<T, string> fmt)
            {
                if (items.Count == 0) return "";
                var lines = items.Take(5).Select((x, i) => $" {i + 1})  {fmt(x)}").ToList();
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



        if (text.Equals("/menu", StringComparison.OrdinalIgnoreCase))
        {
            await SendMainMenu(botClient, chatId, ct);
            return;
        }

        if (text.Equals("/trainer", StringComparison.OrdinalIgnoreCase))
        {
            var login = await dbMsg.TelegramUserLinks
                .Where(x => x.TelegramUserId == tgUserId)
                .Select(x => x.UserLogin)
                .FirstOrDefaultAsync(ct);

            if (login is null)
            {
                await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                return;
            }

            session.Mode = "trainer";
            session.Step = "trainer_choose";
            session.CurrentAssignmentId = null;
            session.TrainerMode = "";
            await SendTrainerChooseMenu(botClient, chatId, ct);
            return;
        }

        // ====== /translate через ITermsService ======
        if (text.StartsWith("/translate", StringComparison.OrdinalIgnoreCase))
        {
            var login = await dbMsg.TelegramUserLinks
                .Where(x => x.TelegramUserId == tgUserId)
                .Select(x => x.UserLogin)
                .FirstOrDefaultAsync(ct);

            if (login is null)
            {
                await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin или /signup.", cancellationToken: ct);
                return;
            }

            if (session.Mode != "translate")
            {
                await botClient.SendMessage(chatId, "Режим перевода не выбран. Откройте /menu → «Режим перевода».", cancellationToken: ct);
                return;
            }

            var arg = text.Length > "/translate".Length ? text.Substring("/translate".Length).Trim() : "";

            if (string.IsNullOrWhiteSpace(arg))
            {
                session.Step = "await_translate_text";
                await botClient.SendMessage(chatId, "Введите термин для перевода одним сообщением.\nДля отмены используйте /cancel.", cancellationToken: ct);
                return;
            }

            await TranslateViaServiceAsync(botClient, termsService, chatId, login, arg, session.TranslateDirection, ct);
            return;
        }

        if (session.Mode == "translate" && session.Step == "await_translate_text")
        {
            if (text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                session.Step = "";
                await botClient.SendMessage(chatId, "Операция отменена.", cancellationToken: ct);
                return;
            }

            var login = await dbMsg.TelegramUserLinks
                .Where(x => x.TelegramUserId == tgUserId)
                .Select(x => x.UserLogin)
                .FirstOrDefaultAsync(ct);

            if (login is null)
            {
                session.Step = "";
                await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                return;
            }

            session.Step = "";
            await TranslateViaServiceAsync(botClient, termsService, chatId, login, text, session.TranslateDirection, ct);
            return;
        }

        // ====== режим тренажера ======
        if (session.Mode == "trainer")
        {
            var login = await dbMsg.TelegramUserLinks
                .Where(x => x.TelegramUserId == tgUserId)
                .Select(x => x.UserLogin)
                .FirstOrDefaultAsync(ct);

            if (login is null)
            {
                await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin.", cancellationToken: ct);
                return;
            }

            if (session.Step == "trainer_choose" || string.IsNullOrWhiteSpace(session.TrainerMode))
            {
                await SendTrainerChooseMenu(botClient, chatId, ct);
                return;
            }

            if (text.Equals("/next", StringComparison.OrdinalIgnoreCase))
            {
                if (session.CurrentAssignmentId is not null)
                    await SkipCurrentAsync(dbMsg, login, session.CurrentAssignmentId.Value, ct);

                session.Step = "";
                session.CurrentAssignmentId = null;

                await SendNextTrainerQuestionAsync(botClient, dbMsg, assignmentsServiceMsg, chatId, login, session, ct);
                return;
            }

            if (session.CurrentAssignmentId is null || session.Step != "await_trainer_answer")
            {
                await SendNextTrainerQuestionAsync(botClient, dbMsg, assignmentsServiceMsg, chatId, login, session, ct);
                return;
            }

            var aId = session.CurrentAssignmentId.Value;

            var resObj = await assignmentsServiceMsg.GetQuestionOrCheckAnswerAsync(
                aId,
                login,
                new AnswerDto(text),
                ct);

            if (resObj is null)
            {
                session.Step = "";
                session.CurrentAssignmentId = null;
                await botClient.SendMessage(chatId, "Карточка не найдена. Дам следующий вопрос.", cancellationToken: ct);
                await SendNextTrainerQuestionAsync(botClient, dbMsg, assignmentsServiceMsg, chatId, login, session, ct);
                return;
            }

            var root = JsonSerializer.SerializeToElement(resObj);

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("forbid", out var forbidEl)
                && forbidEl.ValueKind == JsonValueKind.True)
            {
                session.Step = "";
                session.CurrentAssignmentId = null;
                await botClient.SendMessage(chatId, "Нет доступа к карточке. Дам другой вопрос.", cancellationToken: ct);
                await SendNextTrainerQuestionAsync(botClient, dbMsg, assignmentsServiceMsg, chatId, login, session, ct);
                return;
            }

            bool? correct = TryGetBool(root, "correct");
            var expected = TryGetString(root, "expected");

            var allTranslations = TryGetStringList(root, "allTranslations");
            if (allTranslations.Count == 0 && !string.IsNullOrWhiteSpace(expected))
                allTranslations.Add(expected);

            string answerMsg;
            if (correct == true)
            {
                answerMsg = "✅ Верно!";
            }
            else if (correct == false)
            {
                var variants = allTranslations
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList();

                var lines = variants.Select((x, i) => $"{i + 1}) {H(x)}");
                answerMsg =
                    "❌ Неверно.\n" +
                    (variants.Count > 0
                        ? $"<b>Правильные варианты:</b>\n{string.Join("\n", lines)}"
                        : "<b>Правильный ответ:</b> —");
            }
            else
            {
                answerMsg = "Ответ принят.";
            }

            await botClient.SendMessage(chatId, answerMsg, parseMode: ParseMode.Html, cancellationToken: ct);

            session.Step = "";
            session.CurrentAssignmentId = null;
            await SendNextTrainerQuestionAsync(botClient, dbMsg, assignmentsServiceMsg, chatId, login, session, ct);
            return;
        }

        // если не авторизован — подсказываем
        var linked = await dbMsg.TelegramUserLinks.AnyAsync(x => x.TelegramUserId == tgUserId, ct);
        if (!linked)
        {
            await botClient.SendMessage(chatId, "Авторизация не выполнена. Используйте /signin или /signup.", cancellationToken: ct);
            return;
        }

        await botClient.SendMessage(chatId, "Команда не распознана. Для просмотра доступных команд используйте /start.", cancellationToken: ct);
    }

    private static async Task SkipCurrentAsync(AppDbContext db, string login, int assignmentId, CancellationToken ct)
    {
        var ua = await db.UserAssignments
            .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == assignmentId, ct);

        if (ua == null) return;

        ua.LastAnsweredAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task TranslateViaServiceAsync(
        ITelegramBotClient botClient,
        ITermsService termsService,
        long chatId,
        string login,
        string inputText,
        Direction? preferredDir,
        CancellationToken ct)
    {
        var dto = new TranslateDto(inputText, preferredDir);

        var resObj = await termsService.TranslateAndRememberAsync(login, dto, ct);
        if (resObj is null)
        {
            await botClient.SendMessage(chatId, "Совпадений не найдено.", cancellationToken: ct);
            return;
        }

        var root = JsonSerializer.SerializeToElement(resObj);

        var dirStr = TryGetString(root, "direction");
        var dirLabel =
            dirStr.Equals(nameof(Direction.EnToRu), StringComparison.OrdinalIgnoreCase) ? "EN → RU" :
            dirStr.Equals(nameof(Direction.RuToEn), StringComparison.OrdinalIgnoreCase) ? "RU → EN" : "—";

        var question = TryGetString(root, "question");
        if (string.IsNullOrWhiteSpace(question))
            question = inputText.Trim();

        var variants = TryGetStringList(root, "allTranslations");
        if (variants.Count == 0)
        {
            var one = TryGetString(root, "translation");
            if (!string.IsNullOrWhiteSpace(one)) variants.Add(one);
        }

        var finalList = variants
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        if (finalList.Count == 0)
        {
            await botClient.SendMessage(chatId, "Совпадений не найдено.", cancellationToken: ct);
            return;
        }

        var lines = finalList.Select((x, i) => $"{i + 1}) {H(x)}");

        var html =
            $"<b>Перевод ({H(dirLabel)})</b>\n" +
            $"Термин: <b>{H(question)}</b>\n\n" +
            $"Варианты:\n{string.Join("\n", lines)}";

        await botClient.SendMessage(chatId, html, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private static Task SendTrainerChooseMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Решать карточки (нерешенные)", "trainer_solve") },
            new[] { InlineKeyboardButton.WithCallbackData("Возобновить карточки (сбросить решенные)", "trainer_resume") },
            new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_back") }
        });

        return botClient.SendMessage(
            chatId,
            "Тренажёр:\nЧто делаем?",
            replyMarkup: kb,
            cancellationToken: ct
        );
    }

    private static async Task SendNextTrainerQuestionAsync(
        ITelegramBotClient botClient,
        AppDbContext db,
        IAssignmentsService assignmentsService,
        long chatId,
        string login,
        BotSession session,
        CancellationToken ct)
    {
        var nextId = await db.UserAssignments
            .Where(ua => ua.UserLogin == login && !ua.IsSolved)
            .OrderBy(ua => ua.LastAnsweredAt ?? DateTime.MinValue)
            .Select(ua => ua.AssignmentId)
            .FirstOrDefaultAsync(ct);

        if (nextId == 0)
        {
            session.Step = "";
            session.CurrentAssignmentId = null;

            var kbDone = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Возобновить решённые", "trainer_resume") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_back") }
            });

            await botClient.SendMessage(chatId, "У вас нет нерешённых карточек.", replyMarkup: kbDone, cancellationToken: ct);
            return;
        }

        var qObj = await assignmentsService.GetQuestionOrCheckAnswerAsync(nextId, login, dto: null, ct);
        if (qObj is null)
        {
            session.Step = "";
            session.CurrentAssignmentId = null;
            await botClient.SendMessage(chatId, "Не смог получить вопрос. Попробуйте /next.", cancellationToken: ct);
            return;
        }

        var root = JsonSerializer.SerializeToElement(qObj);

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("forbid", out var forbidEl)
            && forbidEl.ValueKind == JsonValueKind.True)
        {
            session.Step = "";
            session.CurrentAssignmentId = null;
            await botClient.SendMessage(chatId, "Нет доступа к карточке. Попробуйте /next.", cancellationToken: ct);
            return;
        }

        var question = TryGetString(root, "question");
        var dirStr = TryGetString(root, "direction");
        var dirLabel =
            dirStr.Equals(nameof(Direction.EnToRu), StringComparison.OrdinalIgnoreCase) ? "EN → RU" :
            dirStr.Equals(nameof(Direction.RuToEn), StringComparison.OrdinalIgnoreCase) ? "RU → EN" : "—";

        session.CurrentAssignmentId = nextId;
        session.Step = "await_trainer_answer";

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Следующий", "trainer_next") },
            new[] { InlineKeyboardButton.WithCallbackData("Выбор режима", "trainer_choose") }
        });

        var html =
            $"<b>Тренажер ({H(dirLabel)})</b>\n" +
            $"Вопрос: <b>{H(question)}</b>\n\n" +
            $"Введите ответ сообщением.\n" +
            $"(или /next чтобы пропустить)";

        await botClient.SendMessage(chatId, html, parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
    }

    private static Task SendMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("Режим перевода", "menu_translate") },
            new[] { InlineKeyboardButton.WithCallbackData("Режим тренажера", "menu_trainer") }
        });

        return botClient.SendMessage(
            chatId,
            "Меню:\nВыберите режим работы.",
            replyMarkup: kb,
            cancellationToken: ct
        );
    }

    private static Task SendTranslateMenu(ITelegramBotClient botClient, long chatId, Direction? dir, CancellationToken ct)
    {
        var dirLabel = dir switch
        {
            Direction.EnToRu => "EN → RU",
            Direction.RuToEn => "RU → EN",
            _ => "не выбрано"
        };

        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("EN → RU", "tr_dir_enru") },
            new[] { InlineKeyboardButton.WithCallbackData("RU → EN", "tr_dir_ruen") },
            new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_back") }
        });

        return botClient.SendMessage(
            chatId,
            $"Режим перевода.\nТекущее направление: {dirLabel}\n\nКоманда: /translate",
            replyMarkup: kb,
            cancellationToken: ct
        );
    }

    private static string TryGetString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? "";
        return "";
    }

    private static bool? TryGetBool(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static List<string> TryGetStringList(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in p.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.String)
                {
                    var s = (x.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
        }
        return list;
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
