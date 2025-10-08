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

        // GET: api/Terms
        [HttpGet("show-all-terms")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<Term>>> GetTerms()
        {
            return await _context.Terms.ToListAsync();
        }

        // GET: api/Terms/5
        [HttpGet("show-term/{id}")]
        [Authorize]
        public async Task<ActionResult<Term>> GetTerm(int id)
        {
            var term = await _context.Terms.FindAsync(id);

            if (term == null)
            {
                return NotFound();
            }

            return term;
        }

        // PUT: api/Terms/5
        [HttpPut("change-term/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> PutTerm(int id, Term term)
        {
            if (id != term.Id)
            {
                return BadRequest();
            }

            _context.Entry(term).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TermExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Terms
        [HttpPost("create")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<Term>> PostTerm(Term term)
        {
            _context.Terms.Add(term);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetTerm", new { id = term.Id }, term);
        }

        // DELETE: 
        [HttpDelete("{id}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTerm(int id)
        {
            var term = await _context.Terms.FindAsync(id);
            if (term == null)
            {
                return NotFound();
            }

            _context.Terms.Remove(term);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool TermExists(int id)
        {
            return _context.Terms.Any(e => e.Id == id);
        }

        public record TranslateDto(string Text, Direction? Direction);

        [HttpPost("translate")]
        [Authorize]
        public async Task<ActionResult<object>> TranslateAndRemember([FromBody] TranslateDto dto)
        {
            var text = (dto.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return BadRequest("empty text");


            var textLower = text.ToLower();
            var term = await _context.Terms
                .FirstOrDefaultAsync(t => t.En.ToLower() == textLower || t.Ru.ToLower() == textLower);
            if (term is null) return NotFound(new { message = "term not found" });


            Direction direction = dto.Direction ?? (HasCyrillic(text) ? Direction.RuToEn : Direction.EnToRu);
            var translation = direction == Direction.EnToRu ? term.Ru : term.En;


            var login = User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(login))
            {
                var link = await _context.UserTerms
                    .FirstOrDefaultAsync(x => x.UserLogin == login && x.TermId == term.Id);

                if (link is null)
                {
                    _context.UserTerms.Add(new UserTerm
                    {
                        UserLogin = login,
                        TermId = term.Id,
                        LastViewedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    link.LastViewedAt = DateTime.UtcNow;
                }


                var exists = await _context.Assignments.AnyAsync(a =>
                    a.AssignedToLogin == login &&
                    a.TermId == term.Id &&
                    a.Direction == direction &&
                    !a.IsSolved);

                if (!exists)
                {
                    _context.Assignments.Add(new Assignment
                    {
                        TermId = term.Id,
                        AssignedToLogin = login,
                        Direction = direction
                    });
                }

                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                termId = term.Id,
                en = term.En,
                ru = term.Ru,
                direction,
                translation
            });
        }


        [HttpGet("user-terms")] 
        [Authorize]
        public async Task<ActionResult<IEnumerable<object>>> MyTerms()
        {
            var login = User.Identity?.Name!;
            var items = await _context.UserTerms
                .Where(x => x.UserLogin == login)
                .OrderByDescending(x => x.LastViewedAt)
                .Select(x => new
                {
                    x.TermId,
                    x.LastViewedAt,
                    En = x.Term!.En,
                    Ru = x.Term!.Ru,
                    x.Term!.Domain
                })
                .ToListAsync();

            return Ok(items);
        }

        private static bool HasCyrillic(string s) =>
            s.Any(ch => (ch >= 'А' && ch <= 'я') || ch == 'Ё' || ch == 'ё');

    }
}
