// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Linq.Expressions;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;

namespace Sorcha.Register.Storage.InMemory;

/// <summary>
/// In-memory implementation of IRegisterRepository for testing
/// </summary>
public class InMemoryRegisterRepository : IRegisterRepository
{
    private readonly ConcurrentDictionary<string, Models.Register> _registers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TransactionModel>> _transactions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ulong, Docket>> _dockets = new();

    // ===========================
    // Register Operations
    // ===========================

    public Task<bool> IsLocalRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_registers.ContainsKey(registerId));
    }

    public Task<IEnumerable<Models.Register>> GetRegistersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<Models.Register>>(_registers.Values.ToList());
    }

    public Task<IEnumerable<Models.Register>> QueryRegistersAsync(
        Func<Models.Register, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_registers.Values.Where(predicate).ToList().AsEnumerable());
    }

    public Task<Models.Register?> GetRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        _registers.TryGetValue(registerId, out var register);
        return Task.FromResult(register);
    }

    public Task<Models.Register> InsertRegisterAsync(Models.Register newRegister, CancellationToken cancellationToken = default)
    {
        if (!_registers.TryAdd(newRegister.Id, newRegister))
        {
            throw new InvalidOperationException($"Register with ID {newRegister.Id} already exists");
        }

        // Initialize transaction and docket storage for this register
        _transactions.TryAdd(newRegister.Id, new ConcurrentDictionary<string, TransactionModel>());
        _dockets.TryAdd(newRegister.Id, new ConcurrentDictionary<ulong, Docket>());

        return Task.FromResult(newRegister);
    }

    public Task<Models.Register> UpdateRegisterAsync(Models.Register register, CancellationToken cancellationToken = default)
    {
        register.UpdatedAt = DateTime.UtcNow;

        // Use AddOrUpdate to atomically replace the value, avoiding the TOCTOU race
        // that occurred when reading _registers[register.Id] as the comparison value
        // for TryUpdate — the entry could change between the read and the update.
        _registers.AddOrUpdate(
            register.Id,
            _ => throw new InvalidOperationException($"Register {register.Id} does not exist"),
            (_, _) => register);

        return Task.FromResult(register);
    }

    public Task DeleteRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        _registers.TryRemove(registerId, out _);
        _transactions.TryRemove(registerId, out _);
        _dockets.TryRemove(registerId, out _);

        return Task.CompletedTask;
    }

    public Task<int> CountRegistersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_registers.Count);
    }

    // ===========================
    // Docket Operations
    // ===========================

    public Task<IEnumerable<Docket>> GetDocketsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_dockets.TryGetValue(registerId, out var registerDockets))
        {
            return Task.FromResult(Enumerable.Empty<Docket>());
        }

        return Task.FromResult<IEnumerable<Docket>>(registerDockets.Values.OrderBy(d => d.Id).ToList());
    }

    public Task<Docket?> GetDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        if (!_dockets.TryGetValue(registerId, out var registerDockets))
        {
            return Task.FromResult<Docket?>(null);
        }

        registerDockets.TryGetValue(docketId, out var docket);
        return Task.FromResult(docket);
    }

    public Task<Docket> InsertDocketAsync(Docket docket, CancellationToken cancellationToken = default)
    {
        if (!_dockets.TryGetValue(docket.RegisterId, out var registerDockets))
        {
            registerDockets = new ConcurrentDictionary<ulong, Docket>();
            _dockets.TryAdd(docket.RegisterId, registerDockets);
        }

        if (!registerDockets.TryAdd(docket.Id, docket))
        {
            throw new InvalidOperationException($"Docket {docket.Id} already exists in register {docket.RegisterId}");
        }

        return Task.FromResult(docket);
    }

    public async Task UpdateRegisterHeightAsync(
        string registerId,
        uint newHeight,
        CancellationToken cancellationToken = default)
    {
        var register = await GetRegisterAsync(registerId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {registerId} not found");
        }

        register.Height = newHeight;
        register.UpdatedAt = DateTime.UtcNow;

        await UpdateRegisterAsync(register, cancellationToken);
    }

    // ===========================
    // Transaction Operations
    // ===========================

    public Task<IQueryable<TransactionModel>> GetTransactionsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Task.FromResult(Enumerable.Empty<TransactionModel>().AsQueryable());
        }

        return Task.FromResult(registerTransactions.Values.AsQueryable());
    }

    public Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Task.FromResult<TransactionModel?>(null);
        }

        registerTransactions.TryGetValue(transactionId, out var transaction);
        return Task.FromResult(transaction);
    }

    public Task<TransactionModel> InsertTransactionAsync(
        TransactionModel transaction,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(transaction.RegisterId, out var registerTransactions))
        {
            registerTransactions = new ConcurrentDictionary<string, TransactionModel>();
            _transactions.TryAdd(transaction.RegisterId, registerTransactions);
        }

        // Generate DID URI if not already set
        if (string.IsNullOrEmpty(transaction.Id))
        {
            transaction.Id = transaction.GenerateDidUri();
        }

        if (!registerTransactions.TryAdd(transaction.TxId, transaction))
        {
            throw new InvalidOperationException($"Transaction {transaction.TxId} already exists in register {transaction.RegisterId}");
        }

        return Task.FromResult(transaction);
    }

    public Task DeleteTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default)
    {
        if (_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            registerTransactions.TryRemove(txId, out _);
        }
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var transactions = await GetTransactionsAsync(registerId, cancellationToken);
        return transactions.Where(predicate).ToList();
    }

    public async Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        return registerTransactions.Values
            .Where(t => t.DocketNumber == docketId)
            .OrderBy(t => t.TimeStamp)
            .ToList();
    }

    // ===========================
    // Advanced Queries
    // ===========================

    public async Task<IEnumerable<TransactionModel>> GetAllTransactionsByRecipientAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        return registerTransactions.Values
            .Where(t => t.RecipientsWallets.Contains(address))
            .OrderByDescending(t => t.TimeStamp)
            .ToList();
    }

    public async Task<IEnumerable<TransactionModel>> GetAllTransactionsBySenderAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        return registerTransactions.Values
            .Where(t => t.SenderWallet == address)
            .OrderByDescending(t => t.TimeStamp)
            .ToList();
    }

    public async Task<IEnumerable<TransactionModel>> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(prevTxId))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        if (!_transactions.TryGetValue(registerId, out var registerTransactions))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        return registerTransactions.Values
            .Where(t => t.PrevTxId == prevTxId)
            .OrderByDescending(t => t.TimeStamp)
            .ToList();
    }
}
