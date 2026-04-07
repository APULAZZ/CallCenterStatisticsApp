using CallCenterStatisticsApp.Services;
using System.Text;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class MangoImportWindow : Window
{
    private readonly MangoDirectorySyncService _directorySyncService;
    private readonly MangoCallImportService _callImportService;

    public MangoImportWindow(
        MangoDirectorySyncService directorySyncService,
        MangoCallImportService callImportService)
    {
        InitializeComponent();
        _directorySyncService = directorySyncService;
        _callImportService = callImportService;

        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
        {
            MessageBox.Show("Укажите период.");
            return;
        }

        var from = FromDatePicker.SelectedDate.Value.Date;
        var to = ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        var messages = new StringBuilder();
        var hasErrors = false;

        try
        {
            ImportButton.IsEnabled = false;
            BackButton.IsEnabled = false;
            StatusTextBlock.Text = "Выполняется импорт...";

            // 1. Сотрудники
            if (SyncEmployeesCheckBox.IsChecked == true)
            {
                try
                {
                    StatusTextBlock.Text = "Синхронизация сотрудников...";
                    await _directorySyncService.SyncEmployeesAsync();
                    messages.AppendLine("✓ Сотрудники синхронизированы.");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    messages.AppendLine($"✗ Ошибка синхронизации сотрудников: {ex.Message}");
                }
            }

            // 2. Группы
            if (SyncGroupsCheckBox.IsChecked == true)
            {
                try
                {
                    StatusTextBlock.Text = "Синхронизация групп...";
                    await _directorySyncService.SyncGroupsAsync();
                    messages.AppendLine("✓ Группы синхронизированы.");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    messages.AppendLine($"✗ Ошибка синхронизации групп: {ex.Message}");
                }
            }

            // 3. Тематики
            if (SyncTopicsCheckBox.IsChecked == true)
            {
                try
                {
                    StatusTextBlock.Text = "Синхронизация тематик...";
                    await _directorySyncService.SyncTopicsAsync();
                    messages.AppendLine("✓ Тематики синхронизированы.");
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    messages.AppendLine($"✗ Ошибка синхронизации тематик: {ex.Message}");
                }
            }

            // 4. Звонки
            try
            {
                StatusTextBlock.Text = "Импорт звонков...";
                await _callImportService.ImportCallsAsync(from, to);
                messages.AppendLine("✓ Импорт звонков завершен.");
            }
            catch (Exception ex)
            {
                hasErrors = true;
                messages.AppendLine($"✗ Ошибка импорта звонков: {ex.Message}");
            }

            StatusTextBlock.Text = hasErrors
                ? "Импорт завершен с ошибками."
                : "Импорт успешно завершен.";

            MessageBox.Show(
                messages.ToString(),
                hasErrors ? "Импорт завершен с ошибками" : "Импорт завершен",
                MessageBoxButton.OK,
                hasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Критическая ошибка.";
            MessageBox.Show(ex.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportButton.IsEnabled = true;
            BackButton.IsEnabled = true;
        }
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