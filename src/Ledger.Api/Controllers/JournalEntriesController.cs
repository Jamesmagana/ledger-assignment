using Ledger.Api.DTOs.JournalEntries;
using Ledger.Application.Models;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Controllers;

/// <summary>
/// Controller for Journal Entry operations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class JournalEntriesController : ControllerBase
{
    private readonly IJournalEntryService _journalEntryService;

    public JournalEntriesController(IJournalEntryService journalEntryService)
    {
        _journalEntryService = journalEntryService;
    }

    /// <summary>
    /// Posts a new journal entry.
    /// Supports idempotency via ExternalId and request hash.
    /// </summary>
    /// <param name="request">Journal entry creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created or replayed journal entry.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(JournalEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JournalEntryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JournalEntryResponse>> PostJournalEntry(
        [FromBody] CreateJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        // Map API DTO to Application model
        var model = new CreateJournalEntryModel(
            request.ExternalId,
            request.Lines.Select(l => new CreateJournalEntryLineModel(
                l.AccountId,
                l.Direction,
                l.Amount)).ToList());

        var result = await _journalEntryService.PostJournalEntryAsync(model, cancellationToken);

        var response = new JournalEntryResponse(
            result.Entry.Id,
            result.Entry.ExternalId,
            result.Entry.PostedAt,
            result.Entry.CreatedAt,
            result.Entry.UpdatedAt,
            result.Lines.Select(l => new JournalEntryLineResponse(
                l.Id,
                l.AccountId,
                l.Direction,
                l.Amount)).ToList(),
            result.IsIdempotencyReplay);

        // Return 200 OK for idempotent replays, 201 Created for new entries
        if (result.IsIdempotencyReplay)
        {
            return Ok(response);
        }

        return CreatedAtAction(
            nameof(GetJournalEntryById),
            new { id = result.Entry.Id },
            response);
    }

    /// <summary>
    /// Gets a journal entry by ID.
    /// </summary>
    /// <param name="id">Journal entry ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Journal entry if found.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JournalEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JournalEntryResponse>> GetJournalEntryById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _journalEntryService.GetJournalEntryByIdAsync(id, cancellationToken);

        if (result == null)
        {
            return NotFound();
        }

        var response = new JournalEntryResponse(
            result.Entry.Id,
            result.Entry.ExternalId,
            result.Entry.PostedAt,
            result.Entry.CreatedAt,
            result.Entry.UpdatedAt,
            result.Lines.Select(l => new JournalEntryLineResponse(
                l.Id,
                l.AccountId,
                l.Direction,
                l.Amount)).ToList(),
            result.IsIdempotencyReplay);

        return Ok(response);
    }
}

