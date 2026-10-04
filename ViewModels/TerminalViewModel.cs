using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iceman_gui.Core;

namespace iceman_gui.ViewModels;

public partial class TerminalViewModel : ObservableObject
{
    [ObservableProperty]
    private string _commandInput = string.Empty;

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public event Action<string>? OutputLineAdded;

    public TerminalViewModel()
    {
        Pm3ProcessService.Instance.OutputReceived += OnPm3OutputReceived;
    }

    private void OnPm3OutputReceived(object? sender, Pm3OutputEventArgs e)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            TerminalOutput += e.Line + "\n";
            OutputLineAdded?.Invoke(e.Line);
        });
    }

    [RelayCommand]
    public async Task SendCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandInput)) return;

        string cmd = CommandInput.Trim();
        _history.Add(cmd);
        _historyIndex = _history.Count;
        CommandInput = string.Empty;

        TerminalOutput += $"\n[>] {cmd}\n";
        IsBusy = true;

        try
        {
            await Pm3ProcessService.Instance.ExecuteCommandAsync(cmd);
        }
        catch (Exception ex)
        {
            TerminalOutput += $"[!] Command error: {ex.Message}\n";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ClearTerminal()
    {
        TerminalOutput = string.Empty;
    }

    [RelayCommand]
    public void CancelCurrent()
    {
        Pm3ProcessService.Instance.CancelCurrent();
    }

    public string HistoryPrev()
    {
        if (_history.Count == 0) return string.Empty;
        if (_historyIndex > 0) _historyIndex--;
        return _history[_historyIndex];
    }

    public string HistoryNext()
    {
        if (_history.Count == 0) return string.Empty;
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            return _history[_historyIndex];
        }
        _historyIndex = _history.Count;
        return string.Empty;
    }
}
