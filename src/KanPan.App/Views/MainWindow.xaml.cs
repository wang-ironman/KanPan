using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using KanPan.App.ViewModels;
using KanPan.Core.Models;

namespace KanPan.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action _editHoldings;
    private readonly Action<SecurityId?> _recordTrade;
    private readonly Action _refresh;

    public MainWindow(MainViewModel vm, Action editHoldings, Action<SecurityId?> recordTrade, Action refresh)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _editHoldings = editHoldings;
        _recordTrade = recordTrade;
        _refresh = refresh;

        // Rows update in place every few seconds; live sorting keeps a chosen sort order applied.
        if (CollectionViewSource.GetDefaultView(vm.Rows) is ListCollectionView view)
            view.IsLiveSorting = true;
    }

    /// <summary>Raised when the user closes the window, which only hides it to the tray.</summary>
    public event Action? HiddenToTray;

    public bool AllowClose { get; set; }

    public void EnableTestAlert(Action fire)
    {
        TestAlertButton.Visibility = Visibility.Visible;
        TestAlertButton.Click += (_, _) => fire();
    }

    public void FocusSymbol(string symbol)
    {
        var row = _vm.Rows.FirstOrDefault(r => r.Id.Symbol == symbol);
        if (row is null) return;
        HoldingsGrid.SelectedItem = row;
        HoldingsGrid.ScrollIntoView(row);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }
        base.OnClosing(e);
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (CollectionViewSource.GetDefaultView(_vm.Rows) is ListCollectionView view)
        {
            view.LiveSortingProperties.Clear();
            view.LiveSortingProperties.Add(e.Column.SortMemberPath);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => _editHoldings();

    private void OnTradeClick(object sender, RoutedEventArgs e) =>
        _recordTrade((HoldingsGrid.SelectedItem as HoldingRowViewModel)?.Id);

    private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Ignore double-clicks on headers (which sort) and empty space.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(HoldingsGrid, source) is DataGridRow { Item: HoldingRowViewModel row })
            _recordTrade(row.Id);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _refresh();
}
