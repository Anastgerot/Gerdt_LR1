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
    public class TermsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TermsController(AppDbContext context)
        {
            _context = context;
        }


        [HttpGet("show-all-terms")]
        [Authorize]
        public async Task<IActionResult> GetTerms()
        {
            try
            {
                var list = await _context.Terms
                    .AsNoTracking()
                    .OrderBy(t => t.Id)
                    .ToListAsync();

                if (list.Count == 0)
                    return NotFound(new { message = "No terms found." });

                return Ok(list);
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error.", detail: ex.Message, statusCode: 500);
            }
        }


        [HttpGet("show-term/{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetTerm(int id)
        {
            try
            {
                if (id <= 0) 
                    return BadRequest(new { message = "Term id must be positive." });

                var term = await _context.Terms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (term is null) 
                    return NotFound(new { message = $"Term with id={id} not found." });

                return Ok(term);
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error.", detail: ex.Message, statusCode: 500);
            }
        }


        [HttpPut("change-term/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> PutTerm(int id, [FromBody] Term term)
        {
            if (id <= 0) 
                return BadRequest(new { message = "Id must be positive." });

            if (term is null) 
                return BadRequest(new { message = "You must send the term data." });

            try
            {
                var existing = await _context.Terms
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (existing is null)
                    return NotFound(new { message = $"Term with id={id} not found." });

         
                existing.En = term.En?.Trim() ?? existing.En;
                existing.Ru = term.Ru?.Trim() ?? existing.Ru;
                existing.Domain = term.Domain;

                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException ex)
            {
                return Conflict(new { message = "Term with the same EN/RU already exists.", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error.", detail: ex.Message, statusCode: 500);
            }
        }

        [HttpPost("create")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> PostTerm([FromBody] Term term)
        {
            if (term is null) 
                return BadRequest(new { message = "You must send the term data." });

            if (string.IsNullOrWhiteSpace(term.En) || string.IsNullOrWhiteSpace(term.Ru))
                return BadRequest(new { message = "Both translations are required. Please provide non-empty values for 'En' (English) and 'Ru' (Russian)." });

            term.En = term.En.Trim();
            term.Ru = term.Ru.Trim();

            try
            {
                var dup = await _context.Terms
                    .AnyAsync(t => t.En == term.En && t.Ru == term.Ru);

                if (dup) 
                    return Conflict(new { message = "Term with the same EN/RU already exists." });

                _context.Terms.Add(term);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetTerm), new { id = term.Id }, term);
            }

            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error.", detail: ex.Message, statusCode: 500);
            }
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTerm(int id)
        {
            if (id <= 0) 
                return BadRequest(new { message = "Id must be positive." });

            try
            {
                var term = await _context.Terms.FindAsync(id);

                if (term is null) 
                    return NotFound(new { message = $"Term with id={id} not found." });

                _context.Terms.Remove(term);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return Problem(title: "Unexpected server error.", detail: ex.Message, statusCode: 500);
            }
        }


        public record TranslateDto(string Text, Direction? Direction);


        [HttpPost("translate")]
        [Authorize]
        public async Task<IActionResult> TranslateAndRemember([FromBody] TranslateDto? dto)
        {
            try
            {
                if (dto is null || string.IsNullOrWhiteSpace(dto.Text))
                    return BadRequest(new { message = "Text to translate is required."});

                var text = dto.Text.Trim();
                var textLower = text.ToLowerInvariant();

                var term = await _context.Terms
                    .FirstOrDefaultAsync(t => t.En.ToLower() == textLower || t.Ru.ToLower() == textLower);

                if (term is null)
                    return NotFound(new { message = "Term not found for the given text." });

                var direction = dto.Direction ?? (HasCyrillic(text) ? Direction.RuToEn : Direction.EnToRu);

                var question = direction == Direction.EnToRu ? term.En : term.Ru;
                var translation = direction == Direction.EnToRu ? term.Ru : term.En;

                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                // 1) история просмотров пользователя
                var link = await _context.UserTerms
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.TermId == term.Id);

                if (link is null)
                {
                    _context.UserTerms.Add(new UserTerm
                    {
                        UserLogin = login!,
                        TermId = term.Id,
                        LastViewedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    link.LastViewedAt = DateTime.UtcNow;
                }

                // 2) карточка
                var assignment = await _context.Assignments
                    .FirstOrDefaultAsync(a => a.TermId == term.Id && a.Direction == direction);

                if (assignment is null)
                {
                    assignment = new Assignment { TermId = term.Id, Direction = direction };
                    _context.Assignments.Add(assignment);
                    await _context.SaveChangesAsync(); 
                }

                // 3) связь «пользователь-карточка» (если ещё нет)
                var uaExists = await _context.UserAssignments
                    .AnyAsync(ua => ua.UserLogin == login && ua.AssignmentId == assignment.Id);

                if (!uaExists)
                {
                    _context.UserAssignments.Add(new UserAssignment
                    {
                        UserLogin = login!,
                        AssignmentId = assignment.Id,
                        IsSolved = false
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    termId = term.Id,
                    assignmentId = assignment.Id,
                    direction = direction.ToString(),
                    question,
                    translation
                });
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Unexpected server error while translating.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }


        [HttpGet("user-terms")]
        [Authorize]
        public async Task<IActionResult> MyTerms()
        {
            try
            {
                var login = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(login))
                    return Unauthorized(new { message = "User is not authenticated." });

                var items = await _context.UserTerms
                    .Where(x => x.UserLogin == login)
                    .OrderByDescending(x => x.LastViewedAt)
                    .Select(x => new
                    {
                        x.TermId,
                        x.LastViewedAt,
                        En = x.Term!.En,
                        Ru = x.Term!.Ru,
                        Domain = x.Term!.Domain
                    })
                    .ToListAsync();

                if (items.Count == 0)
                    return NotFound(new { message = "You have no viewed terms yet." });

                return Ok(items);
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Unexpected server error while reading your terms.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }


        private static bool HasCyrillic(string s) =>
            s.Any(ch => (ch >= 'А' && ch <= 'я') || ch == 'Ё' || ch == 'ё');

    }
}
