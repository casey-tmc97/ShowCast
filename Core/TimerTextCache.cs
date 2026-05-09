using System;
using System.Collections.Concurrent;

namespace ShowCast.Core;

/// <summary>
/// Shared live-text store written by TimerViewModels and read by PageRenderer.
/// Keeps Engine and ViewModels decoupled — both reference Core.
/// </summary>
public static class TimerTextCache
{
    public static readonly ConcurrentDictionary<Guid, string> Values = new();

    /// <summary>Fired on the UI thread after any timer value changes.</summary>
    public static event Action? Changed;

    /// <summary>Update a timer value and notify subscribers.</summary>
    public static void Update(Guid id, string text)
    {
        Values[id] = text;
        Changed?.Invoke();
    }

    public static void Remove(Guid id)
    {
        Values.TryRemove(id, out _);
    }
}
