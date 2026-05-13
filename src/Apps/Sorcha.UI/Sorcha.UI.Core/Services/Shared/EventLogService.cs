// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sorcha.UI.Core.Services;

public enum EventType
{
    Info,
    Success,
    Warning,
    Error
}

public class EventLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public EventType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Icon { get; set; } = string.Empty;
}

public class EventLogService
{
    private readonly List<EventLogEntry> _events = new();
    private const int MaxEvents = 100; // Keep only the last 100 events

    public event Action? OnEventAdded;

    public IReadOnlyList<EventLogEntry> GetRecentEvents(int count = 10)
    {
        return _events.OrderByDescending(e => e.Timestamp).Take(count).ToList();
    }

    public IReadOnlyList<EventLogEntry> GetAllEvents()
    {
        return _events.OrderByDescending(e => e.Timestamp).ToList();
    }

    public void LogInfo(string message, string? details = null)
    {
        AddEvent(new EventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = EventType.Info,
            Message = message,
            Details = details,
            Icon = MudBlazor.Icons.Material.Filled.Info
        });
    }

    public void LogSuccess(string message, string? details = null)
    {
        AddEvent(new EventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = EventType.Success,
            Message = message,
            Details = details,
            Icon = MudBlazor.Icons.Material.Filled.CheckCircle
        });
    }

    public void LogWarning(string message, string? details = null)
    {
        AddEvent(new EventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = EventType.Warning,
            Message = message,
            Details = details,
            Icon = MudBlazor.Icons.Material.Filled.Warning
        });
    }

    public void LogError(string message, string? details = null)
    {
        AddEvent(new EventLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Type = EventType.Error,
            Message = message,
            Details = details,
            Icon = MudBlazor.Icons.Material.Filled.Error
        });
    }

    private void AddEvent(EventLogEntry entry)
    {
        _events.Add(entry);

        // Keep only the most recent events
        if (_events.Count > MaxEvents)
        {
            _events.RemoveAt(0);
        }

        OnEventAdded?.Invoke();
    }

    public void Clear()
    {
        _events.Clear();
        OnEventAdded?.Invoke();
    }
}
