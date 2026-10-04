using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;
using iceman_gui.ViewModels;

namespace iceman_gui;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPageTag))
            {
                SyncNavigation(_viewModel.SelectedPageTag);
            }
        };

        Loaded += (s, e) =>
        {
            SyncNavigation(_viewModel.SelectedPageTag);
        };
    }

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem item && item.Tag is string tag)
        {
            if (tag != "terminal")
            {
                _viewModel.NavigateTo(tag);
            }
        }
    }

    private void SyncNavigation(string pageTag)
    {
        foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            item.IsActive = (item.Tag as string == pageTag);
        }
    }
}