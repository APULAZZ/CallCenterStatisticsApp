using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using System.Windows;
using System.Windows.Controls;
namespace CallCenterStatisticsApp.UI.Views;
public partial class ImportPage : UserControl
{
    private readonly MangoSynchronizationService _synchronization;
    private readonly BusyService _busy;
    public ImportPage(MangoSynchronizationService synchronization, BusyService busy) { InitializeComponent(); _synchronization = synchronization; _busy = busy; FromDatePicker.SelectedDate = DateTime.Today.AddDays(-6); ToDatePicker.SelectedDate = DateTime.Today; }
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate is not DateTime from || ToDatePicker.SelectedDate is not DateTime to) return;
        try { using var operation = _busy.Begin("Синхронизация данных с Mango…"); RunButton.IsEnabled = false; StatusTextBlock.Text = "Синхронизация…"; StatusTextBlock.Text = await _synchronization.SynchronizeAsync(from.Date, to.Date.AddDays(1).AddSeconds(-1), SyncEmployeesCheckBox.IsChecked == true, SyncTopicsCheckBox.IsChecked == true); }
        catch (Exception ex) { StatusTextBlock.Text = "Синхронизация не завершена."; MessageBox.Show(ex.Message, "Ошибка синхронизации", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { RunButton.IsEnabled = true; }
    }
}
