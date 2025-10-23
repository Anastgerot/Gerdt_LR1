using Gerdt_LR1.Models;
using Gerdt_LR1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gerdt_LR1.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AssignmentsController : ControllerBase
{
    private readonly IAssignmentsService _svc;
    public AssignmentsController(IAssignmentsService svc) => _svc = svc;

    [HttpGet("show-all-assigments")]
    [Authorize]
    public async Task<IActionResult> GetAssignments(CancellationToken ct)
    {
        try
        {
            var list = await _svc.GetAllAsync(ct);

            if (list.Count == 0)
                return NotFound(new { message = "No assignments found." });

            return Ok(list);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while reading assignments.", detail: ex.Message, statusCode: 500); }
    }

    [HttpGet("show-assigment/{id:int}")]
    [Authorize]
    public async Task<IActionResult> GetAssignment(int id, CancellationToken ct)
    {
        try
        {
            if (id <= 0) 
                return BadRequest(new { message = "The identifier in the URL must be a positive number." });

            var a = await _svc.GetByIdAsync(id, ct);

            if (a is null) return NotFound(new { message = $"Assignment with id={id} not found." });
            return Ok(a);
        }
        catch (Exception ex) { 
            return Problem(title: $"Unexpected server error while reading assignment id={id}.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpGet("user-assigments")]
    [Authorize]
    public async Task<IActionResult> UserAssignments([FromQuery] bool? solved, CancellationToken ct)
    {
        try
        {
            var login = User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var items = await _svc.GetUserAssignmentsAsync(login, solved, ct);

            if (items.Count == 0) 
                return NotFound(new { message = "No assignments found for the current user." });
            return Ok(items);
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while reading user assignments.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteAssignment(int id, CancellationToken ct)
    {
        try
        {
            if (id <= 0)
                return BadRequest(new { message = "The identifier in the URL must be a positive number." });

            var ok = await _svc.DeleteAsync(id, ct);

            if (!ok) 
                return NotFound(new { message = $"Assignment with id={id} not found." });
            return NoContent();
        }
        catch (Exception ex) { return Problem(title: $"Unexpected server error while deleting assignment id={id}.", detail: ex.Message, statusCode: 500); }
    }

    [HttpPost("{id:int}/question-answer")]
    [Authorize]
    public async Task<IActionResult> QuestionOrAnswer(int id, [FromBody] AnswerDto? dto, CancellationToken ct)
    {
        try
        {
            if (id <= 0) 
                return BadRequest(new { message = "The identifier in the URL must be a positive number." });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var a = await _svc.GetByIdAsync(id, ct);
            if (a is null)
                return NotFound(new { message = $"Assignment with id={id} not found." });

            var linked = await _svc.IsLinkedAsync(id, login, ct);
            if (!linked) return Forbid();

            var res = await _svc.GetQuestionOrCheckAnswerAsync(id, login, dto, ct);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Problem(title: $"Unexpected server error while processing assignment id={id}.",
               detail: ex.Message,
               statusCode: 500);
        }
    }


    [HttpPost("{id:int}/switch-direction")]
    [Authorize]
    public async Task<IActionResult> SwitchDirection(int id, CancellationToken ct)
    {
        try
        {
            if (id <= 0)
                return BadRequest(new { message = "The identifier in the URL must be a positive number." });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var assignment = await _svc.GetByIdAsync(id, ct);
            if (assignment is null)
                return NotFound(new { message = $"Assignment with id={id} not found." });

            var linked = await _svc.IsLinkedAsync(id, login, ct);
            if (!linked)
                return Conflict(new { message = "You don't have an assignment linked with the current id. Check your input." });

            var payload = await _svc.SwitchDirectionAsync(id, login, ct);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            return Problem(
                title: $"Unexpected server error while switching direction for assignment id={id}.",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    [HttpPost("create-assignment")]
    [Authorize]
    public async Task<IActionResult> CreateAssignment([FromBody] AssignForMeDto? dto, CancellationToken ct)
    {
        try
        {
            if (dto is null || dto.TermId <= 0) 
                return BadRequest(new { message = "TermId must be a positive number." });

            var login = User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var (result, status) = await _svc.CreateForUserAsync(login, dto, ct);
            return status switch
            {
                201 => CreatedAtAction(nameof(GetAssignment), new { id = ((dynamic)result).assignmentId }, result),
                404 => NotFound(new { message = ((dynamic)result).msg }),
                409 => Conflict(new { message = ((dynamic)result).msg }),
                _ => Ok(result)
            };
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while creating assignment.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost("generate")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Generate([FromBody] GenerateAssignmentsDto? dto, CancellationToken ct)
    {
        try
        {
            if (dto is null || dto.Count <= 0) 
                return BadRequest(new { message = "Count must be greater than 0." });

            var (items, requested, created) = await _svc.GenerateAsync(dto, ct);

            if (!items.Any()) 
                return Conflict(new { message = "No terms without assignments for the specified direction." });

            return Ok(new { requested, created, direction = dto.Direction.ToString(), items });
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while generating assignments.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost("add-assigments-to-user")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddAssigmentsToUser([FromBody] AddAssignmentsDto? dto, CancellationToken ct)
    {
        try
        {
            if (dto is null || dto.Count <= 0) 
                return BadRequest(new { message = "Count must be greater than 0." });

            if (string.IsNullOrWhiteSpace(dto.UserLogin)) 
                return BadRequest(new { message = "UserLogin is required." });

            var (items, createdLinks) = await _svc.AddAssignmentsToUserAsync(dto, ct);

            if (createdLinks == 0)
                return Conflict(new { message = "No new assignments to link for this user." });

            return Ok(new { user = dto.UserLogin, 
                requestedLinks = dto.Count, 
                createdLinks, items });
        }
        catch (Exception ex) { 
            return Problem(title: "Unexpected server error while adding assignments to user.", 
                detail: ex.Message, 
                statusCode: 500); }
    }

    [HttpPost("{id:int}/mark-unsolved")]
    [Authorize]
    public async Task<IActionResult> MarkUnsolved(int id, [FromBody] ResetAssignmentDto? dto, CancellationToken ct)
    {
        try
        {
            if (id <= 0)
                return BadRequest(new { message = "The identifier in the URL must be a positive number." });

            var login = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(login))
                return Unauthorized(new { message = "User is not authenticated." });

            var assignment = await _svc.GetByIdAsync(id, ct);
            if (assignment is null)
                return NotFound(new { message = $"Assignment with id={id} not found." });

            var linked = await _svc.IsLinkedAsync(id, login, ct);
            if (!linked)
                return Forbid();

            var payload = await _svc.MarkUnsolvedAsync(id, login, dto, ct);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            return Problem(
                title: $"Unexpected server error while marking assignment id={id} as unsolved.",
                detail: ex.Message,
                statusCode: 500);
        }
    }

}
