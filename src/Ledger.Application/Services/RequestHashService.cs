using Ledger.Application.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ledger.Application.Services;

/// <summary>
/// Service for computing SHA-256 hash of canonical request payload.
/// Ensures deterministic hashing by using canonical JSON format.
/// </summary>
public class RequestHashService : IRequestHashService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ComputeHash(CreateJournalEntryModel request)
    {
        // Create canonical representation
        var canonical = CreateCanonicalRepresentation(request);
        
        // Compute SHA-256 hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        
        // Convert to hex string (64 characters)
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string CreateCanonicalRepresentation(CreateJournalEntryModel request)
    {
        // Sort lines for canonical order:
        // 1. AccountId (ascending)
        // 2. Direction (ascending)
        // 3. Amount (ascending)
        var sortedLines = request.Lines
            .OrderBy(l => l.AccountId)
            .ThenBy(l => l.Direction)
            .ThenBy(l => l.Amount)
            .ToList();

        // Create canonical object (exclude null ExternalId from hash)
        var canonicalObject = new
        {
            ExternalId = request.ExternalId, // Include even if null for consistency
            Lines = sortedLines.Select(l => new
            {
                AccountId = l.AccountId,
                Direction = l.Direction,
                Amount = l.Amount
            }).ToList()
        };

        // Serialize to compact JSON (no whitespace)
        return JsonSerializer.Serialize(canonicalObject, JsonOptions);
    }
}

