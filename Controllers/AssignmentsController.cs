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

        [HttpGet("{id:int}/question")]
        [Authorize]
        public async Task<IActionResult> GetQuestion(int id)
        {
            var a = await _context.Assignments
                .Include(x => x.Term)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return NotFound();

            // только владелец или админ
            var isOwner = string.Equals(User.Identity?.Name, a.AssignedToLogin, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !User.IsInRole("admin")) return Forbid();

            if (a.Term is null) return Problem("Term is missing for this assignment.", statusCode: 500);

            var question = a.Direction == Direction.EnToRu ? a.Term.En : a.Term.Ru;
            var from = a.Direction == Direction.EnToRu ? "EN" : "RU";
            var to = a.Direction == Direction.EnToRu ? "RU" : "EN";

            return Ok(new
            {
                assignmentId = a.Id,
                termId = a.TermId,
                direction = a.Direction.ToString(),
                from,
                to,
                question
            });
        }

        public record AnswerDto(string Answer);

        [HttpPost("{id:int}/answer")]
        [Authorize]
        [Consumes("application/json")]
        public async Task<IActionResult> Answer(int id, [FromBody] AnswerDto dto)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Answer))
                return BadRequest(new { message = "answer is required" });

            var a = await _context.Assignments
                .Include(x => x.Term)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a is null)
                return NotFound(new { message = "assignment not found" });

            // только владелец или админ
            var isOwner = string.Equals(User.Identity?.Name, a.AssignedToLogin, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !User.IsInRole("admin"))
                return Forbid();

            if (a.Term is null)
                return Problem("Term is missing for this assignment. Please fix data.", statusCode: 500);

            var wasSolved = a.IsSolved;
            var correct = a.CheckAnswer(dto.Answer);

            if (correct && !wasSolved)
            {
                var user = await _context.Users.FindAsync(a.AssignedToLogin);
                user?.AddPoints(1);
            }

            await _context.SaveChangesAsync();

            var question = a.Direction == Direction.EnToRu ? a.Term.En : a.Term.Ru;

            return Ok(new
            {
                assignmentId = a.Id,
                termId = a.TermId,
                direction = a.Direction.ToString(),
                question,                     
                yourAnswer = dto.Answer,
                expected = a.ExpectedAnswer,
                correct
            });
        }

        public record AssignForMeDto(int TermId, Direction? Direction);    

        [HttpPost("create-assigment")]
        [Authorize]
        public async Task<ActionResult<Assignment>> CreateForMe([FromBody] AssignForMeDto dto)
        {
            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

            var term = await _context.Terms.FindAsync(dto.TermId);
            if (term is null) return NotFound(new { message = "term not found" });

            var dir = dto.Direction ?? Direction.EnToRu;


            var duplicate = await _context.Assignments
                .AnyAsync(a => a.AssignedToLogin == login && a.TermId == dto.TermId && a.Direction == dir && !a.IsSolved);
            if (duplicate) return Conflict(new { message = "assignment already exists" });

            var a = new Assignment
            {
                TermId = term.Id,
                AssignedToLogin = login,
                Direction = dir
            };

            _context.Assignments.Add(a);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAssignment), new { id = a.Id }, a);
        }


        [HttpPost("{id:int}/switch-direction")]
        [Authorize]
        public async Task<ActionResult<object>> SwitchDirection(int id)
        {
            var a = await _context.Assignments.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return NotFound();

            var isOwner = string.Equals(User.Identity?.Name, a.AssignedToLogin, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !User.IsInRole("admin")) return Forbid();

            a.SwitchDirection();
            await _context.SaveChangesAsync();

            return Ok(new { id = a.Id, newDirection = a.Direction, isSolved = a.IsSolved });
        }


    }
}
