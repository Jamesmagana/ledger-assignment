using Ledger.Application.Exceptions;
using Ledger.Application.Models;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Moq;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class JournalEntryServiceTests
{
    private readonly Mock<IJournalEntryRepository> _journalEntryRepositoryMock;
    private readonly Mock<IAccountRepository> _accountRepositoryMock;
    private readonly Mock<IRequestHashService> _hashServiceMock;
    private readonly JournalEntryService _service;

    public JournalEntryServiceTests()
    {
        _journalEntryRepositoryMock = new Mock<IJournalEntryRepository>();
        _accountRepositoryMock = new Mock<IAccountRepository>();
        _hashServiceMock = new Mock<IRequestHashService>();
        _service = new JournalEntryService(
            _journalEntryRepositoryMock.Object,
            _accountRepositoryMock.Object,
            _hashServiceMock.Object);
    }

    [Fact]
    public async Task PostJournalEntryAsync_LessThan2Lines_ThrowsValidationException()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId, LineDirection.Debit, 100.00m)
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("INVALID_LINE_COUNT", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_AmountZero_ThrowsValidationException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 0m),
                new(accountId2, LineDirection.Credit, 0m)
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("INVALID_AMOUNT", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_AmountScaleGreaterThan4_ThrowsValidationException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.12345m), // 5 decimal places
                new(accountId2, LineDirection.Credit, 100.12345m)
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("INVALID_AMOUNT_SCALE", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_UnbalancedEntry_ThrowsValidationException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 200.00m) // Unbalanced
            });

        var account1 = new Account("Cash", AccountType.Asset, true) { Id = accountId1 };
        var account2 = new Account("Revenue", AccountType.Revenue, true) { Id = accountId2 };

        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account1);
        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account2);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("UNBALANCED_ENTRY", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_AccountNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("ACCOUNT_NOT_FOUND", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_AccountInactive_ThrowsValidationException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var account1 = new Account("Cash", AccountType.Asset, false) { Id = accountId1 }; // Inactive
        var account2 = new Account("Revenue", AccountType.Revenue, true) { Id = accountId2 };

        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account1);
        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account2);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("ACCOUNT_INACTIVE", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_ValidEntry_ReturnsJournalEntryResult()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var account1 = new Account("Cash", AccountType.Asset, true) { Id = accountId1 };
        var account2 = new Account("Revenue", AccountType.Revenue, true) { Id = accountId2 };

        var journalEntry = new JournalEntry("ext-123", "hash123", DateTime.UtcNow)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var lines = new List<JournalEntryLine>
        {
            new(journalEntry.Id, accountId1, LineDirection.Debit, 100.00m) { Id = Guid.NewGuid() },
            new(journalEntry.Id, accountId2, LineDirection.Credit, 100.00m) { Id = Guid.NewGuid() }
        };

        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account1);
        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account2);

        _hashServiceMock
            .Setup(s => s.ComputeHash(It.IsAny<CreateJournalEntryModel>()))
            .Returns("hash123");

        _journalEntryRepositoryMock
            .Setup(r => r.FindByExternalIdAsync("ext-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntry?)null);

        _journalEntryRepositoryMock
            .Setup(r => r.AddWithLinesAsync(It.IsAny<JournalEntry>(), It.IsAny<IReadOnlyList<JournalEntryLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((journalEntry, lines));

        // Act
        var result = await _service.PostJournalEntryAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(journalEntry.Id, result.Entry.Id);
        Assert.Equal(2, result.Lines.Count);
        Assert.False(result.IsIdempotencyReplay);
    }

    [Fact]
    public async Task PostJournalEntryAsync_IdempotentReplay_ReturnsExistingEntry()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var existingEntry = new JournalEntry("ext-123", "hash123", DateTime.UtcNow)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var existingLines = new List<JournalEntryLine>
        {
            new(existingEntry.Id, accountId1, LineDirection.Debit, 100.00m) { Id = Guid.NewGuid() },
            new(existingEntry.Id, accountId2, LineDirection.Credit, 100.00m) { Id = Guid.NewGuid() }
        };

        _hashServiceMock
            .Setup(s => s.ComputeHash(It.IsAny<CreateJournalEntryModel>()))
            .Returns("hash123");

        _journalEntryRepositoryMock
            .Setup(r => r.FindByExternalIdAsync("ext-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _journalEntryRepositoryMock
            .Setup(r => r.GetLinesByJournalEntryIdAsync(existingEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingLines);

        // Act
        var result = await _service.PostJournalEntryAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingEntry.Id, result.Entry.Id);
        Assert.True(result.IsIdempotencyReplay);
        _journalEntryRepositoryMock.Verify(
            r => r.AddWithLinesAsync(It.IsAny<JournalEntry>(), It.IsAny<IReadOnlyList<JournalEntryLine>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostJournalEntryAsync_IdempotencyConflict_ThrowsConflictException()
    {
        // Arrange
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            "ext-123",
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.00m),
                new(accountId2, LineDirection.Credit, 100.00m)
            });

        var existingEntry = new JournalEntry("ext-123", "different-hash", DateTime.UtcNow)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _hashServiceMock
            .Setup(s => s.ComputeHash(It.IsAny<CreateJournalEntryModel>()))
            .Returns("hash123");

        _journalEntryRepositoryMock
            .Setup(r => r.FindByExternalIdAsync("ext-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.PostJournalEntryAsync(request));
        
        Assert.Equal("DUPLICATE_EXTERNAL_ID", exception.ReasonCode);
    }

    [Fact]
    public async Task PostJournalEntryAsync_ExactDecimalBalance_ValidatesCorrectly()
    {
        // Arrange - Test exact decimal matching (no rounding tolerance)
        var accountId1 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var request = new CreateJournalEntryModel(
            null,
            new List<CreateJournalEntryLineModel>
            {
                new(accountId1, LineDirection.Debit, 100.1234m),
                new(accountId2, LineDirection.Credit, 100.1234m) // Exact match
            });

        var account1 = new Account("Cash", AccountType.Asset, true) { Id = accountId1 };
        var account2 = new Account("Revenue", AccountType.Revenue, true) { Id = accountId2 };

        var journalEntry = new JournalEntry(null, "hash123", DateTime.UtcNow)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var lines = new List<JournalEntryLine>
        {
            new(journalEntry.Id, accountId1, LineDirection.Debit, 100.1234m) { Id = Guid.NewGuid() },
            new(journalEntry.Id, accountId2, LineDirection.Credit, 100.1234m) { Id = Guid.NewGuid() }
        };

        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account1);
        _accountRepositoryMock
            .Setup(r => r.GetByIdAsync(accountId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account2);

        _hashServiceMock
            .Setup(s => s.ComputeHash(It.IsAny<CreateJournalEntryModel>()))
            .Returns("hash123");

        _journalEntryRepositoryMock
            .Setup(r => r.AddWithLinesAsync(It.IsAny<JournalEntry>(), It.IsAny<IReadOnlyList<JournalEntryLine>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((journalEntry, lines));

        // Act
        var result = await _service.PostJournalEntryAsync(request);

        // Assert - Should succeed with exact decimal match
        Assert.NotNull(result);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task GetJournalEntryByIdAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid();

        _journalEntryRepositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntry?)null);

        // Act
        var result = await _service.GetJournalEntryByIdAsync(id);

        // Assert
        Assert.Null(result);
    }
}

