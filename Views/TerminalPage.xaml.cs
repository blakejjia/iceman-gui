using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using iceman_gui.Core;
using iceman_gui.ViewModels;

namespace iceman_gui.Views;

public partial class TerminalPage : UserControl
{
    public TerminalPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalViewModel vm)
        {
            vm.OutputLineAdded += OnOutputLineAdded;
        }
    }

    private void OnOutputLineAdded(string line)
    {
        Dispatcher.Invoke(() =>
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            var runs = AnsiColorParser.ParseToRuns(line);
            foreach (var r in runs)
            {
                p.Inlines.Add(r);
            }
            TerminalBox.Document.Blocks.Add(p);

            // Limit buffer size to ~1500 lines to preserve memory
            while (TerminalBox.Document.Blocks.Count > 1500)
            {
                TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);
            }

            TerminalBox.ScrollToEnd();
        });
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TerminalViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            if (vm.SendCommandCommand.CanExecute(null))
            {
                vm.SendCommandCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            string prev = vm.HistoryPrev();
            if (!string.IsNullOrEmpty(prev))
            {
                vm.CommandInput = prev;
                InputBox.CaretIndex = vm.CommandInput.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            string next = vm.HistoryNext();
            vm.CommandInput = next;
            InputBox.CaretIndex = vm.CommandInput.Length;
            e.Handled = true;
        }
    }

    private void QuickCmd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cmd && DataContext is TerminalViewModel vm)
        {
            vm.CommandInput = cmd;
            if (vm.SendCommandCommand.CanExecute(null))
            {
                vm.SendCommandCommand.Execute(null);
            }
        }
    }
}
