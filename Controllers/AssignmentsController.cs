using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gerdt_LR1.Data;
using Gerdt_LR1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gerdt_LR1.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssignmentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AssignmentsController(AppDbContext context)
        {
            _context = context;
        }

        // GET
        [HttpGet("show-all-assigments")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<Assignment>>> GetAssignments()
        {
            try
            {
                var list = await _context.Assignments
                    .AsNoTracking()
                    .OrderBy(a => a.Id)
                    .ToListAsync();

                if (list.Count == 0)
                    return NotFound(new { message = "No assignments found." });

                return Ok(list);
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error while reading assignments.",
                               detail: ex.Message, statusCode: 500);
            }
        }

        // GET
        [HttpGet("show-assigment/{id}")]
        [Authorize]
        public async Task<ActionResult<Assignment>> GetAssignment(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "The identifier in the URL must be a positive number." });

                var assignment = await _context.Assignments.FindAsync(id);

                if (assignment is null)
                    return NotFound(new { message = $"Assignment with id={id} not found." });

                return Ok(assignment);
            }
            catch (Exception ex)
            {
                return Problem(title: $"Unexpected server error while reading assignment id={id}.",
                               detail: ex.Message, statusCode: 500);
            }
        }

        [HttpGet("user-assigments")]
        [Authorize]
        public async Task<IActionResult> UserAssignments([FromQuery] bool? solved = null)
        {
            try
            {
                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                var query = _context.UserAssignments
                    .Where(ua => ua.UserLogin == login)
                    .Include(ua => ua.Assignment)!.ThenInclude(a => a.Term)
                    .AsNoTracking()
                    .AsQueryable();

                if (solved.HasValue)
                    query = query.Where(ua => ua.IsSolved == solved.Value);

                query = query.OrderBy(ua => ua.IsSolved);

                var items = await query
                    .Select(ua => new
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
                    })
                    .ToListAsync();

                if (items.Count == 0)
                    return NotFound(new { message = "No assignments found for the current user." });

                return Ok(items);
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error while reading user assignments.",
                               detail: ex.Message, statusCode: 500);
            }
        }


        // DELETE
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteAssignment(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "The identifier in the URL must be a positive number." });

                var assignment = await _context.Assignments.FindAsync(id);
                if (assignment is null)
                    return NotFound(new { message = $"Assignment with id={id} not found." });

                _context.Assignments.Remove(assignment);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                return Problem(title: $"Unexpected server error while deleting assignment id={id}.",
                               detail: ex.Message, statusCode: 500);
            }
        }

        public record AnswerDto(string? Answer);

        [HttpPost("{id:int}/question-answer")]
        [Authorize]
        public async Task<IActionResult> QuestionOrAnswer(int id, [FromBody] AnswerDto? dto)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "The identifier in the URL must be a positive number." });

                var a = await _context.Assignments
                    .Include(x => x.Term)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (a is null)
                    return NotFound(new { message = $"Assignment with id={id} not found." });

                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });


                var ua = await _context.UserAssignments
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id);

                if (ua is null)
                    return Forbid();

                var question = a.Direction == Direction.EnToRu ? a.Term.En : a.Term.Ru;

                // 1) Нет ответа — вернуть «пустую» карточку-вопрос
                if (dto is null || string.IsNullOrWhiteSpace(dto.Answer))
                {
                    return Ok(new
                    {
                        assignmentId = a.Id,
                        termId = a.TermId,
                        direction = a.Direction.ToString(),
                        question,
                        yourAnswer = (string?)null,
                        expected = (string?)null,
                        correct = (bool?)null,
                        isSolved = ua.IsSolved
                    });
                }

                // 2) Есть ответ — обновить статистику и проверить
                ua.Attempts += 1;
                ua.LastAnsweredAt = DateTime.UtcNow;

                var wasSolved = ua.IsSolved;
                var correct = a.CheckAnswer(dto.Answer);

                if (correct && !wasSolved)
                {
                    ua.IsSolved = true;
                    ua.SolvedAt = DateTime.UtcNow;

                    var user = await _context.Users.FindAsync(login);
                    user?.AddPoints(1);
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    assignmentId = a.Id,
                    termId = a.TermId,
                    direction = a.Direction.ToString(),
                    question,
                    yourAnswer = dto.Answer,
                    expected = a.Term.Translate(a.Direction),
                    correct,
                    isSolved = ua.IsSolved
                });
            }
            catch (Exception ex)
            {
                return Problem(title: $"Unexpected server error while processing assignment id={id}.",
                               detail: ex.Message, statusCode: 500);
            }
        }

        [HttpPost("{id:int}/switch-direction")]
        [Authorize]
        public async Task<IActionResult> SwitchDirection(int id)
        {

            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "The identifier in the URL must be a positive number." });

                var a = await _context.Assignments.FirstOrDefaultAsync(x => x.Id == id);
                if (a is null)
                    return NotFound(new { message = $"Assignment with id={id} not found." });

                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                // 1) Противоположная карточка
                var newDir = a.Direction == Direction.EnToRu ? Direction.RuToEn : Direction.EnToRu;

                var opposite = await _context.Assignments
                    .FirstOrDefaultAsync(x => x.TermId == a.TermId && x.Direction == newDir);

                if (opposite is null)
                {
                    opposite = new Assignment { TermId = a.TermId, Direction = newDir };
                    _context.Assignments.Add(opposite);
                    await _context.SaveChangesAsync(); 
                }

                // 2) Перелинковать пользователя
                var uaCurrent = await _context.UserAssignments
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id);

                var uaOpposite = await _context.UserAssignments
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == opposite.Id);

                if (uaOpposite is not null)
                {
                    // уже есть связь с противоположной — просто сброс прогресса
                    uaOpposite.IsSolved = false;
                    uaOpposite.SolvedAt = null;
                }
                else if (uaCurrent is not null)
                {
                    // есть связь с текущей — перелинкуем
                    uaCurrent.AssignmentId = opposite.Id;
                    uaCurrent.IsSolved = false;
                    uaCurrent.SolvedAt = null;
                }
                else
                {
                    return Conflict(new { message = "You don't have an assignment linked with the current id. Check your input." });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    assignmentId = opposite.Id,
                    termId = opposite.TermId,
                    newDirection = newDir.ToString(),
                    isSolved = false
                });
            }
            catch (Exception ex)
            {
                return Problem(title: $"Unexpected server error while switching direction for assignment id={id}.",
                               detail: ex.Message, statusCode: 500);
            }
        }


        public record AssignForMeDto(int TermId, Direction? Direction);

        [HttpPost("create-assignment")]
        [Authorize]
        public async Task<IActionResult> CreateAssignment([FromBody] AssignForMeDto? dto)
        {
            try
            {
                if (dto.TermId <= 0)
                    return BadRequest(new { message = "TermId must be a positive number." });

                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                var term = await _context.Terms.FindAsync(dto.TermId);
                if (term is null)
                    return NotFound(new { message = $"Term with id={dto.TermId} not found." });

                var dir = dto.Direction ?? Direction.EnToRu;

                var assignment = await _context.Assignments
                    .FirstOrDefaultAsync(a => a.TermId == term.Id && a.Direction == dir);

                if (assignment is null)
                {
                    assignment = new Assignment { TermId = term.Id, Direction = dir };
                    _context.Assignments.Add(assignment);
                    await _context.SaveChangesAsync();
                }

                // 1) Проверка, не привязана ли уже эта карточка к пользователю
                var linkExists = await _context.UserAssignments
                    .AnyAsync(ua => ua.UserLogin == login && ua.AssignmentId == assignment.Id);

                if (linkExists)
                    return Conflict(new { message = "This assignment is already linked to the current user." });

                // 2) Создание связи UserAssignment
                _context.UserAssignments.Add(new UserAssignment
                {
                    UserLogin = login!,
                    AssignmentId = assignment.Id,
                    IsSolved = false
                });
                await _context.SaveChangesAsync();

                // Вернём краткую карточку-вопрос
                var question = dir == Direction.EnToRu ? term.En : term.Ru;

                return CreatedAtAction(nameof(GetAssignment), new { id = assignment.Id }, new
                {
                    assignmentId = assignment.Id,
                    termId = term.Id,
                    direction = dir.ToString(),
                    question
                });
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error while creating assignment.",
                               detail: ex.Message, statusCode: 500);
            }
        }


        public record GenerateAssignmentsDto(int Count, Direction Direction);

        [HttpPost("generate")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Generate([FromBody] GenerateAssignmentsDto? dto)
        {
            try
            {

                if (dto.Count <= 0)
                    return BadRequest(new { message = "Count must be greater than 0." });

                var dir = dto.Direction; 

                // Ищем термины, по которым нет карточки с указанным направлением
                var candidateIds = await _context.Terms
                    .Where(t => !_context.Assignments.Any(a => a.TermId == t.Id && a.Direction == dir))
                    .OrderBy(t => t.Id)
                    .Take(dto.Count)
                    .Select(t => t.Id)
                    .ToListAsync();

                if (candidateIds.Count == 0)
                    return Conflict(new { message = "No terms without assignments for the specified direction." });

                var toCreate = candidateIds.Select(id => new Assignment
                {
                    TermId = id,
                    Direction = dir
                }).ToList();

                _context.Assignments.AddRange(toCreate);

                await _context.SaveChangesAsync();


                var items = toCreate.Select(a => new
                {
                    assignmentId = a.Id,
                    termId = a.TermId,
                    direction = a.Direction.ToString()
                });

                return Ok(new
                {
                    requested = dto.Count,
                    created = toCreate.Count,
                    direction = dir.ToString(),
                    items
                });
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error while generating assignments.",
                               detail: ex.Message, statusCode: 500);
            }
        }


        public record AddAssigmentsDto(int count, string UserLogin);

        [HttpPost("add-assigments-to-user")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AddAssigmentsToUser([FromBody] AddAssigmentsDto? dto)
        {
            try
            {
                if (dto.count <= 0)
                    return BadRequest(new { message = "Count must be greater than 0." });

                if (string.IsNullOrWhiteSpace(dto.UserLogin))
                    return BadRequest(new { message = "UserLogin is required." });

                var login = dto.UserLogin.Trim();

                // Пользователь существует?
                var userExists = await _context.Users.AsNoTracking().AnyAsync(u => u.Login == login);
                if (!userExists)
                    return NotFound(new { message = $"User '{login}' not found." });

                // Выбираем Assignment, которых у пользователя ещё нет
                var candidateAssignments = await _context.Assignments.AsNoTracking()
                    .Where(a => !_context.UserAssignments
                        .Any(ua => ua.UserLogin == login && ua.AssignmentId == a.Id))
                    .OrderBy(a => a.Id)
                    .Take(dto.count)
                    .Select(a => new { a.Id, a.TermId, a.Direction })
                    .ToListAsync();

                if (candidateAssignments.Count == 0)
                    return Conflict(new { message = "No new assignments to link for this user." });

                var links = candidateAssignments.Select(a => new UserAssignment
                {
                    UserLogin = login,
                    AssignmentId = a.Id,
                    IsSolved = false
                }).ToList();

                _context.UserAssignments.AddRange(links);
                await _context.SaveChangesAsync();

                var items = candidateAssignments.Select(a => new
                {
                    assignmentId = a.Id,
                    termId = a.TermId,
                    direction = a.Direction.ToString()
                });

                return Ok(new
                {
                    user = login,
                    requestedLinks = dto.count,
                    createdLinks = links.Count,
                    items
                });
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error while adding assignments to user.",
                               detail: ex.Message, statusCode: 500);
            }
        }


        public record ResetAssignmentDto(bool ResetAttempts = true, bool ClearTimestamps = true);

        [HttpPost("{id:int}/mark-unsolved")]
        [Authorize]
        public async Task<IActionResult> MarkUnsolved(int id, [FromBody] ResetAssignmentDto? dto)
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "The identifier in the URL must be a positive number." });

                // 1) Карточка существует?
                var a = await _context.Assignments.FirstOrDefaultAsync(x => x.Id == id);
                if (a is null)
                    return NotFound(new { message = $"Assignment with id={id} not found." });

                // 2) Текущий пользователь
                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                // 3) Связь пользователь-карточка
                var ua = await _context.UserAssignments
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id);

                if (ua is null)
                    return Forbid();

                // 4) Сброс статуса
                ua.IsSolved = false;
                ua.SolvedAt = null;

                // Параметры сброса 
                var reset = dto ?? new ResetAssignmentDto();
                if (reset.ResetAttempts)
                    ua.Attempts = 0;

                if (reset.ClearTimestamps)
                    ua.LastAnsweredAt = null;

                await _context.SaveChangesAsync();

                // 5) Вернём краткую информацию
                return Ok(new
                {
                    assignmentId = ua.AssignmentId,
                    user = ua.UserLogin,
                    isSolved = ua.IsSolved,
                    attempts = ua.Attempts,
                    solvedAt = ua.SolvedAt,
                    lastAnsweredAt = ua.LastAnsweredAt
                });
            }
            catch (Exception ex)
            {
                return Problem(
                    title: $"Unexpected server error while marking assignment id={id} as unsolved.",
                     detail: ex.Message, statusCode: 500);
            }
        }

    }
}
