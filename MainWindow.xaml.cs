using Wpf.Ui.Controls;
using iceman_gui.ViewModels;

namespace iceman_gui;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}