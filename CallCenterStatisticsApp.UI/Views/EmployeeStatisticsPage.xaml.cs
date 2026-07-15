using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using System.Windows;
using System.Windows.Controls;
namespace CallCenterStatisticsApp.UI.Views;
public partial class EmployeeStatisticsPage : UserControl
{
    private readonly CallStatisticsService _statistics;
    private readonly BusyService _busy;
    private bool _loaded;
    public EmployeeStatisticsPage(CallStatisticsService statistics, BusyService busy) { InitializeComponent(); _statistics = statistics; _busy = busy; FromDatePicker.SelectedDate = DateTime.Today.AddDays(-6); ToDatePicker.SelectedDate = DateTime.Today; }
    public async Task LoadAsync() { if (_loaded) return; await LoadDataAsync(); _loaded = true; }
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadDataAsync();
    private async Task LoadDataAsync() { if (FromDatePicker.SelectedDate is not DateTime from || ToDatePicker.SelectedDate is not DateTime to) return; using var operation = _busy.Begin("Формируем отчёт по сотрудникам…"); var rows = await _statistics.GetEmployeeStatsAsync(from.Date, to.Date.AddDays(1).AddSeconds(-1)); StatsDataGrid.ItemsSource = rows; SummaryTextBlock.Text = $"Сотрудников в отчёте: {rows.Count:N0}"; }
}
