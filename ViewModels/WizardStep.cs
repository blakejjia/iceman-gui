using CommunityToolkit.Mvvm.ComponentModel;

namespace iceman_gui.ViewModels;

public partial class WizardStep : ObservableObject
{
    public int Number { get; }
    public string Title { get; }
    public bool IsNotLast { get; set; } = true;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _canGo = true;

    public string Glyph => IsDone ? "✓" : Number.ToString();

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    public WizardStep(int number, string title)
    {
        Number = number;
        Title = title;
    }
}
