using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using GlassCraft.App.ViewModels;

namespace GlassCraft.App.Views;

public partial class AgentView : UserControl
{
    private AgentViewModel? _vm;

    public AgentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
        _vm = DataContext as AgentViewModel;
        if (_vm is not null)
            _vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ChatList.ItemCount > 0)
                ChatList.ScrollIntoView(ChatList.ItemCount - 1);
        }, DispatcherPriority.Background);
    }
}
