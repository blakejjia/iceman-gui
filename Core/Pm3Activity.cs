using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace iceman_gui.Core;

/// <summary>
/// Global "PM3 is working" indicator. View models wrap long operations in a
/// scope so the bottom status bar can show an activity ring and the current
/// operation. Nested scopes are reference-counted.
/// </summary>
public sealed partial class Pm3Activity : ObservableObject
{
    public static Pm3Activity Instance { get; } = new();

    private readonly object _sync = new();
    private readonly List<string> _stack = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _description = string.Empty;

    private Pm3Activity()
    {
    }

    public IDisposable Begin(string description)
    {
        string label = string.IsNullOrWhiteSpace(description) ? "Working" : description;

        lock (_sync)
        {
            _stack.Add(label);
            Description = label;
            IsBusy = true;
        }

        return new Scope(this, label);
    }

    private void End(string description)
    {
        lock (_sync)
        {
            int index = _stack.LastIndexOf(description);
            if (index >= 0)
            {
                _stack.RemoveAt(index);
            }

            Description = _stack.Count > 0 ? _stack[^1] : string.Empty;
            IsBusy = _stack.Count > 0;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Pm3Activity _owner;
        private readonly string _description;
        private bool _disposed;

        public Scope(Pm3Activity owner, string description)
        {
            _owner = owner;
            _description = description;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.End(_description);
        }
    }
}
