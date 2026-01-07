using Ledger.Application.Exceptions;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Moq;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class AccountServiceTests
{
    private readonly Mock<IAccountRepository> _repositoryMock;
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        _repositoryMock = new Mock<IAccountRepository>();
        _service = new AccountService(_repositoryMock.Object);
    }

    [Fact]
    public async Task CreateAccountAsync_ValidInput_ReturnsAccount()
    {
        // Arrange
        var name = "Cash";
        var type = AccountType.Asset;
        var isActive = true;

        var expectedAccount = new Account(name, type, isActive)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.FindByNameAsync(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedAccount);

        // Act
        var result = await _service.CreateAccountAsync(name, type, isActive);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(name, result.Name);
        Assert.Equal(type, result.Type);
        Assert.Equal(isActive, result.IsActive);
        _repositoryMock.Verify(r => r.FindByNameAsync(name, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAccountAsync_EmptyName_ThrowsValidationException()
    {
        // Arrange
        var name = "";
        var type = AccountType.Asset;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAccountAsync(name, type, true));
        
        Assert.Equal("INVALID_NAME", exception.ReasonCode);
    }

    [Fact]
    public async Task CreateAccountAsync_WhitespaceName_ThrowsValidationException()
    {
        // Arrange
        var name = "   ";
        var type = AccountType.Asset;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAccountAsync(name, type, true));
        
        Assert.Equal("INVALID_NAME", exception.ReasonCode);
    }

    [Fact]
    public async Task CreateAccountAsync_NameTooLong_ThrowsValidationException()
    {
        // Arrange
        var name = new string('A', 201); // 201 characters
        var type = AccountType.Asset;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAccountAsync(name, type, true));
        
        Assert.Equal("INVALID_NAME", exception.ReasonCode);
    }

    [Fact]
    public async Task CreateAccountAsync_DuplicateName_ThrowsConflictException()
    {
        // Arrange
        var name = "Cash";
        var type = AccountType.Asset;
        var existingAccount = new Account(name, type)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.FindByNameAsync(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAccountAsync(name, type, true));
        
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", exception.ReasonCode);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAccountAsync_DuplicateNameCaseInsensitive_ThrowsConflictException()
    {
        // Arrange
        var name = "cash";
        var existingName = "CASH";
        var type = AccountType.Asset;
        var existingAccount = new Account(existingName, type)
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.FindByNameAsync(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAccountAsync(name, type, true));
        
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", exception.ReasonCode);
    }

    [Fact]
    public async Task GetAccountByIdAsync_ValidId_ReturnsAccount()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expectedAccount = new Account("Cash", AccountType.Asset)
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedAccount);

        // Act
        var result = await _service.GetAccountByIdAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        _repositoryMock.Verify(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAccountByIdAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        // Act
        var result = await _service.GetAccountByIdAsync(id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAccountByIdAsync_EmptyId_ThrowsValidationException()
    {
        // Arrange
        var id = Guid.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetAccountByIdAsync(id));
        
        Assert.Equal("INVALID_ACCOUNT_ID", exception.ReasonCode);
    }

    [Fact]
    public async Task GetAllAccountsAsync_ReturnsAllAccounts()
    {
        // Arrange
        var accounts = new List<Account>
        {
            new Account("Cash", AccountType.Asset) { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Account("Revenue", AccountType.Revenue) { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);

        // Act
        var result = await _service.GetAllAccountsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        _repositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAccountIsActiveAsync_ValidId_ReturnsUpdatedAccount()
    {
        // Arrange
        var id = Guid.NewGuid();
        var existingAccount = new Account("Cash", AccountType.Asset, isActive: true)
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var updatedAccount = existingAccount.WithIsActive(false);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        _repositoryMock
            .Setup(r => r.HasJournalLinesAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedAccount);

        // Act
        var result = await _service.UpdateAccountIsActiveAsync(id, false);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsActive);
        _repositoryMock.Verify(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAccountIsActiveAsync_NotFound_ThrowsNotFoundException()
    {
        // Arrange
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Account?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAccountIsActiveAsync(id, false));
        
        Assert.Equal("ACCOUNT_NOT_FOUND", exception.ReasonCode);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAccountIsActiveAsync_EmptyId_ThrowsValidationException()
    {
        // Arrange
        var id = Guid.Empty;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.UpdateAccountIsActiveAsync(id, false));
        
        Assert.Equal("INVALID_ACCOUNT_ID", exception.ReasonCode);
    }
}

