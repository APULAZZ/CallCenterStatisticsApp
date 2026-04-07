using CallCenterStatisticsApp.Services;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class TopicEnrichmentWindow : Window
{
    private readonly MangoCallTopicEnrichmentService _topicEnrichmentService;

    public TopicEnrichmentWindow(MangoCallTopicEnrichmentService topicEnrichmentService)
    {
        InitializeComponent();
        _topicEnrichmentService = topicEnrichmentService;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BatchSizeTextBox.Text, out var batchSize) || batchSize <= 0)
        {
            MessageBox.Show("Укажите корректное количество звонков для обработки.");
            return;
        }

        try
        {
            RunButton.IsEnabled = false;
            BackButton.IsEnabled = false;
            StatusTextBlock.Text = "Обновление тематик...";

            var updated = await _topicEnrichmentService.EnrichTopicsAsync(batchSize);

            StatusTextBlock.Text = $"Готово. Тематики обновлены у {updated} звонков.";

            MessageBox.Show(
                $"Тематики обновлены у {updated} звонков.",
                "Обновление завершено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Ошибка при обновлении тематик.";
            MessageBox.Show(ex.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RunButton.IsEnabled = true;
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