using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using System.Windows;
using System.Windows.Controls;
namespace CallCenterStatisticsApp.UI.Views;
public partial class TopicEnrichmentPage : UserControl
{
    private readonly MangoCallTopicEnrichmentService _service;
    private readonly BusyService _busy;
    public TopicEnrichmentPage(MangoCallTopicEnrichmentService service, BusyService busy) { InitializeComponent(); _service = service; _busy = busy; }
    private async void RunButton_Click(object sender, RoutedEventArgs e) { if (!int.TryParse(BatchSizeTextBox.Text, out var batchSize) || batchSize < 1) { MessageBox.Show("Укажите положительное количество звонков.", "Речевая аналитика"); return; } try { using var operation = _busy.Begin("Обновление категорий речевой аналитики…"); RunButton.IsEnabled = false; StatusTextBlock.Text = "Обновление категорий…"; var updated = await _service.EnrichTopicsAsync(batchSize); StatusTextBlock.Text = $"Готово. Категории обновлены у {updated} звонков."; } catch (Exception ex) { StatusTextBlock.Text = "Не удалось обновить категории."; MessageBox.Show(ex.Message, "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Warning); } finally { RunButton.IsEnabled = true; } }
}
