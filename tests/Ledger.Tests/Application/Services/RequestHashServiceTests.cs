using Ledger.Application.Models;
using Ledger.Application.Services;
using Ledger.Domain.Enums;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class RequestHashServiceTests
{
    private readonly RequestHashService _service;

    public RequestHashServiceTests()
    {
        _service = new RequestHashService();
    }

    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        // Arrange
        var request1 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(Guid.NewGuid(), LineDirection.Debit, 100.00m),
                new(Guid.NewGuid(), LineDirection.Credit, 100.00m)
            });

        var request2 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(request1.Lines[0].AccountId, LineDirection.Debit, 100.00m),
                new(request1.Lines[1].AccountId, LineDirection.Credit, 100.00m)
            });

        // Act
        var hash1 = _service.ComputeHash(request1);
        var hash2 = _service.ComputeHash(request2);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 hex string is 64 characters
    }

    [Fact]
    public void ComputeHash_DifferentInput_ReturnsDifferentHash()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var request1 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var request2 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 200.00m), // Different amount
                new(accountId2, LineDirection.Credit, 200.00m)
            });

        // Act
        var hash1 = _service.ComputeHash(request1);
        var hash2 = _service.ComputeHash(request2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentExternalId_ReturnsDifferentHash()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var request1 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var request2 = new CreateJournalEntryModel(
            "ext-456", // Different ExternalId
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        // Act
        var hash1 = _service.ComputeHash(request1);
        var hash2 = _service.ComputeHash(request2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_CanonicalOrder_ReturnsSameHash()
    {
        // Arrange - same lines in different order should produce same hash (canonical)
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var request1 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var request2 = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId2, LineDirection.Credit, 100.00m), // Different order
                new(accountId1, LineDirection.Debit, 100.00m)
            });

        // Act
        var hash1 = _service.ComputeHash(request1);
        var hash2 = _service.ComputeHash(request2);

        // Assert - should be same due to canonical sorting
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NullExternalId_HandlesCorrectly()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();

        var request1 = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var request2 = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        // Act
        var hash1 = _service.ComputeHash(request1);
        var hash2 = _service.ComputeHash(request2);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }
}

