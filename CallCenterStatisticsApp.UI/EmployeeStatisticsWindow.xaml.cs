using CallCenterStatisticsApp.Services;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class EmployeeStatisticsWindow : Window
{
    private readonly CallStatisticsService _statisticsService;

    public EmployeeStatisticsWindow(CallStatisticsService statisticsService)
    {
        InitializeComponent();
        _statisticsService = statisticsService;

        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
        {
            MessageBox.Show("Укажите период.");
            return;
        }

        var from = FromDatePicker.SelectedDate.Value.Date;
        var to = ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        var rows = await _statisticsService.GetEmployeeStatsAsync(from, to);

        StatsDataGrid.ItemsSource = rows;
        SummaryTextBlock.Text = $"Сотрудников в отчете: {rows.Count}";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Owner?.Show();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (Owner != null && !Owner.IsVisible)
            Owner.Show();
    }
}