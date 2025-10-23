using Gerdt_LR1.Models;
using Gerdt_LR1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gerdt_LR1.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TermsController : ControllerBase
{
    private readonly ITermsService _svc;
    public TermsController(ITermsService svc) => _svc = svc;

    [HttpGet("show-all-terms")]
    [Authorize]
    public async Task<IActionResult> GetTerms(CancellationToken ct)
    {
        try
        {
            var list = await _svc.GetAllAsync(ct);
            if (list.Count == 0) 
                return NotFound(new { message = "No terms found." });

            return Ok(list);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpGet("show-term/{id:int}")]
    [Authorize]
    public async Task<IActionResult> GetTerm(int id, CancellationToken ct)
    {
        try
        {
            if (id <= 0) 
                return BadRequest(new { message = "Term id must be positive." });

            var term = await _svc.GetByIdAsync(id, ct);
            if (term is null) 
                return NotFound(new { message = $"Term with id={id} not found." });

            return Ok(term);
        }
        catch (Exception ex) {
            return Problem(title: "Unexpected server error.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPut("change-term/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> PutTerm(int id, [FromBody] Term term, CancellationToken ct)
    {
        if (id <= 0) 
            return BadRequest(new { message = "Id must be positive." });

        if (term is null) 
            return BadRequest(new { message = "You must send the term data." });

        try
        {
            var (ok, conflictMsg) = await _svc.UpdateAsync(id, term, ct);
            if (!ok && conflictMsg is null) 
                return NotFound(new { message = $"Term with id={id} not found." });

            if (!ok) return Conflict(new { message = conflictMsg });

            return NoContent();
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost("create")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> PostTerm([FromBody] Term term, CancellationToken ct)
    {
        if (term is null) 
            return BadRequest(new { message = "You must send the term data." });

        if (string.IsNullOrWhiteSpace(term.En) || string.IsNullOrWhiteSpace(term.Ru))
            return BadRequest(new { message = "Both translations are required. Please provide non-empty values for 'En' and 'Ru'." });

        try
        {
            var (created, conflict) = await _svc.CreateAsync(term, ct);
            if (created is null) 
                return Conflict(new { message = conflict });

            return CreatedAtAction(nameof(GetTerm), new { id = created.Id }, created);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteTerm(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest(new { message = "Id must be positive." });
        try
        {
            var ok = await _svc.DeleteAsync(id, ct);
            if (!ok) return NotFound(new { message = $"Term with id={id} not found." });

            return NoContent();
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error.",
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost("translate")]
    [Authorize]
    public async Task<IActionResult> TranslateAndRemember([FromBody] TranslateDto? dto, CancellationToken ct)
    {
        try
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Text))
                return BadRequest(new { message = "Text to translate is required." });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var res = await _svc.TranslateAndRememberAsync(login, dto, ct);
            if (res is null) 
                return NotFound(new { message = "Term not found for the given text." });

            return Ok(res);
        }
        catch (Exception ex)
        {
            return Problem(title: "Unexpected server error while translating.", 
                detail: ex.Message,
                 statusCode: 500);
        }
    }

    [HttpGet("user-terms")]
    [Authorize]
    public async Task<IActionResult> MyTerms(CancellationToken ct)
    {
        try
        {
            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var items = await _svc.GetMyTermsAsync(login, ct);
            if (items.Count == 0) return NotFound(new { message = "You have no viewed terms yet." });
            return Ok(items);
        }
        catch (Exception ex)
        {
            return Problem(title: "Unexpected server error while reading your terms.", 
                detail: ex.Message,
                 statusCode: 500);
        }
    }
}
