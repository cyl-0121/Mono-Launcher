using Avalonia.Controls;
using GlassCraft.App.ViewModels;

namespace GlassCraft.App.Views;

public partial class VersionsView : UserControl
{
    public VersionsView()
    {
        InitializeComponent();
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } combo && DataContext is VersionsViewModel vm)
            vm.SetFilter(combo.SelectedIndex);
    }
}
