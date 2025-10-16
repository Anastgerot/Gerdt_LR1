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
            return await _context.Assignments.ToListAsync();
        }

        // GET
        [HttpGet("show-assigment/{id}")]
        [Authorize]
        public async Task<ActionResult<Assignment>> GetAssignment(int id)
        {
            var assignment = await _context.Assignments.FindAsync(id);

            if (assignment == null)
            {
                return NotFound();
            }

            return assignment;
        }

        [HttpGet("user-assigments")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<object>>> UserAssignments([FromQuery] bool? solved = null)
        {
            var login = User.Identity?.Name!;
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

            var query = _context.UserAssignments
                .Where(ua => ua.UserLogin == login)
                .Include(ua => ua.Assignment)!.ThenInclude(a => a.Term)
                .AsQueryable();

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
                               ? ua.Assignment.Term.En
                               : ua.Assignment.Term.Ru,
            
                    expected = ua.IsSolved
                               ? ua.Assignment.Term.Translate(ua.Assignment.Direction)
                               : null
                })
                .ToListAsync();

            return Ok(items);
        }


        // DELETE
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteAssignment(int id)
        {
            var assignment = await _context.Assignments.FindAsync(id);
            if (assignment == null)
            {
                return NotFound();
            }

            _context.Assignments.Remove(assignment);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public record AnswerDto(string? Answer);

        [HttpPost("{id:int}/question-answer")]
        [Authorize]
        public async Task<IActionResult> QuestionOrAnswer(int id, [FromBody] AnswerDto? dto)
        {
            var a = await _context.Assignments.Include(x => x.Term).FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return NotFound(new { message = "assignment not found" });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

            var ua = await _context.UserAssignments
                .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id);

            if (ua is null && !User.IsInRole("admin")) return Forbid();

            if (a.Term is null) return Problem("Term is missing for this assignment.", statusCode: 500);

            var question = a.Direction == Direction.EnToRu ? a.Term.En : a.Term.Ru;

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
                    isSolved = ua?.IsSolved
                });
            }

            if (ua is not null)
            {
                ua.Attempts += 1;
                ua.LastAnsweredAt = DateTime.UtcNow;
            }

            var wasSolved = ua?.IsSolved ?? false;
            var correct = a.CheckAnswer(dto.Answer);

            if (correct && ua is not null && !wasSolved)
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
                isSolved = ua?.IsSolved
            });
        }

        [HttpPost("{id:int}/switch-direction")]
        [Authorize]
        public async Task<IActionResult> SwitchDirection(int id)
        {
            var a = await _context.Assignments.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return NotFound(new { message = "Assignment not found" });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();


            var newDir = a.Direction == Direction.EnToRu ? Direction.RuToEn : Direction.EnToRu;

            var opposite = await _context.Assignments
                .FirstOrDefaultAsync(x => x.TermId == a.TermId && x.Direction == newDir);

            if (opposite is null)
            {
                opposite = new Assignment { TermId = a.TermId, Direction = newDir };
                _context.Assignments.Add(opposite);
                await _context.SaveChangesAsync(); 
            }

            // 4) Перелинковываем текущего пользователя:
            // - если есть связь с текущей карточкой — перелинкуем её на противоположную
            // - если уже есть связь с противоположной — просто сбросим прогресс
            // - если вообще нет связей — ошибка
            var uaCurrent = await _context.UserAssignments
                .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == a.Id);

            var uaOpposite = await _context.UserAssignments
                .FirstOrDefaultAsync(x => x.UserLogin == login && x.AssignmentId == opposite.Id);

            if (uaOpposite is not null)
            {
                uaOpposite.IsSolved = false;
                uaOpposite.SolvedAt = null;
            }
            else if (uaCurrent is not null)
            {
                uaCurrent.AssignmentId = opposite.Id;
                uaCurrent.IsSolved = false;
                uaCurrent.SolvedAt = null;
            }
            else
            {
                return Conflict(new { message = "User doesn't have an assignment with the current id. Check your input." });
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


        public record AssignForMeDto(int TermId, Direction? Direction);

        [HttpPost("create-assignment")]
        [Authorize]
        public async Task<IActionResult> CreateAssignment([FromBody] AssignForMeDto dto)
        {
            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

            var term = await _context.Terms.FindAsync(dto.TermId);
            if (term is null) return NotFound(new { message = "Term not found" });

            var dir = dto.Direction ?? Direction.EnToRu;

            // 1) Создаем карточку по (TermId, Direction)
            var assignment = await _context.Assignments
                .FirstOrDefaultAsync(a => a.TermId == term.Id && a.Direction == dir);

            if (assignment is null)
            {
                assignment = new Assignment { TermId = term.Id, Direction = dir };
                _context.Assignments.Add(assignment);
                await _context.SaveChangesAsync(); 
            }

            // 2) Проверяем, не привязана ли уже эта карточка к пользователю
            var linkExists = await _context.UserAssignments
                .AnyAsync(ua => ua.UserLogin == login && ua.AssignmentId == assignment.Id);

            if (linkExists)
                return Conflict(new { message = "Assignment already linked to this user" });

            // 3) Создаем связь UserAssignment
            var ua = new UserAssignment
            {
                UserLogin = login,
                AssignmentId = assignment.Id,
                IsSolved = false
            };
            _context.UserAssignments.Add(ua);
            await _context.SaveChangesAsync();


            var question = dir == Direction.EnToRu ? term.En : term.Ru;
            return CreatedAtAction(nameof(GetAssignment), new { id = assignment.Id }, new
            {
                assignmentId = assignment.Id,
                termId = term.Id,
                direction = dir.ToString(),
                question
            });
        }


        public record GenerateAssignmentsDto(int Count, Direction Direction);

        [HttpPost("generate")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Generate([FromBody] GenerateAssignmentsDto dto)
        {
            if (dto.Count <= 0)
                return BadRequest(new { message = "Count must be > 0" });

            var dir = dto.Direction;

            // Найти термины, у которых НЕТ карточки с этим направлением
            var candidates = await _context.Terms
                .Where(t => !_context.Assignments
                .Any(a => a.TermId == t.Id && a.Direction == dir))
                .OrderBy(t => t.Id)
                .Take(dto.Count)
                .Select(t => t.Id)
                .ToListAsync();

            if (candidates.Count == 0)
                return Conflict(new { message = "No terms without assignments for the specified direction." });

            var toCreate = candidates.Select(id => new Assignment
            {
                TermId = id,
                Direction = dir
            }).ToList();

            _context.Assignments.AddRange(toCreate);

            await _context.SaveChangesAsync();

            var result = toCreate.Select(a => new
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
                items = result
            });
        }




    }
}
