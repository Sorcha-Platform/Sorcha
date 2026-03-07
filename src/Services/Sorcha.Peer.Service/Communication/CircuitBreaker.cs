// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Circuit breaker pattern implementation for resilient communication
/// </summary>
public class CircuitBreaker
{
    private readonly ILogger<CircuitBreaker> _logger;
    private readonly string _name;
    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    private CircuitState _state = CircuitState.Closed;
    private int _failureCount = 0;
    private DateTimeOffset _lastFailureTime = DateTimeOffset.MinValue;
    private DateTimeOffset _openedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public CircuitBreaker(
        ILogger<CircuitBreaker> logger,
        string name,
        int failureThreshold = 5,
        TimeSpan? resetTimeout = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _failureThreshold = failureThreshold;
        _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Gets the current circuit state
    /// </summary>
    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                // Check if we should transition from Open to HalfOpen
                if (_state == CircuitState.Open &&
                    DateTimeOffset.UtcNow - _openedAt >= _resetTimeout)
                {
                    _logger.LogInformation(
                    "Circuit breaker half-open for {Name}, allowing probe connection",
                    _name);
                    _state = CircuitState.HalfOpen;
                    _failureCount = 0;
                }

                return _state;
            }
        }
    }

    /// <summary>
    /// Executes an operation through the circuit breaker
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        if (!CanExecute())
        {
            throw new CircuitBreakerOpenException($"Circuit breaker {_name} is open");
        }

        try
        {
            var result = await operation();
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure();
            throw new CircuitBreakerException($"Operation failed in circuit breaker {_name}", ex);
        }
    }

    /// <summary>
    /// Executes an operation with a fallback
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback)
    {
        try
        {
            return await ExecuteAsync(operation);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning("Circuit breaker {Name} is open, using fallback", _name);
            return await fallback();
        }
        catch (CircuitBreakerException ex)
        {
            _logger.LogWarning(ex, "Operation failed in circuit breaker {Name}, using fallback", _name);
            return await fallback();
        }
    }

    /// <summary>
    /// Checks if the circuit allows execution
    /// </summary>
    private bool CanExecute()
    {
        var currentState = State; // This may transition Open -> HalfOpen
        return currentState != CircuitState.Open;
    }

    /// <summary>
    /// Records a successful operation
    /// </summary>
    internal void OnSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _logger.LogInformation(
                    "Circuit breaker closed for {Name}, connection restored",
                    _name);
                _state = CircuitState.Closed;
                _failureCount = 0;
            }
            else if (_state == CircuitState.Closed)
            {
                // Reset failure count on success
                _failureCount = 0;
            }
        }
    }

    /// <summary>
    /// Records a failed operation
    /// </summary>
    internal void OnFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTimeOffset.UtcNow;

            _logger.LogWarning("Circuit breaker {Name} failure {Count}/{Threshold}",
                _name, _failureCount, _failureThreshold);

            if (_state == CircuitState.HalfOpen)
            {
                // Immediate trip on failure in HalfOpen
                _logger.LogWarning(
                    "Circuit breaker opened for {Name} after probe failure in half-open state",
                    _name);
                _state = CircuitState.Open;
                _openedAt = DateTimeOffset.UtcNow;
            }
            else if (_state == CircuitState.Closed && _failureCount >= _failureThreshold)
            {
                _logger.LogWarning(
                    "Circuit breaker opened for {Name} after {FailureCount} failures",
                    _name, _failureCount);
                _state = CircuitState.Open;
                _openedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Manually resets the circuit breaker
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _logger.LogInformation("Circuit breaker {Name} manually reset", _name);
            _state = CircuitState.Closed;
            _failureCount = 0;
            _lastFailureTime = DateTimeOffset.MinValue;
            _openedAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Gets circuit breaker statistics
    /// </summary>
    public CircuitBreakerStats GetStats()
    {
        lock (_lock)
        {
            return new CircuitBreakerStats
            {
                Name = _name,
                State = _state,
                FailureCount = _failureCount,
                FailureThreshold = _failureThreshold,
                LastFailureTime = _lastFailureTime,
                OpenedAt = _openedAt,
                ResetTimeout = _resetTimeout
            };
        }
    }
}

/// <summary>
/// Circuit breaker state
/// </summary>
public enum CircuitState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, rejecting calls
    HalfOpen   // Testing if circuit can close
}

/// <summary>
/// Circuit breaker statistics
/// </summary>
public class CircuitBreakerStats
{
    public string Name { get; set; } = string.Empty;
    public CircuitState State { get; set; }
    public int FailureCount { get; set; }
    public int FailureThreshold { get; set; }
    public DateTimeOffset LastFailureTime { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public TimeSpan ResetTimeout { get; set; }
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when operation fails in circuit breaker
/// </summary>
public class CircuitBreakerException : Exception
{
    public CircuitBreakerException(string message, Exception innerException)
        : base(message, innerException) { }
}
