using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<MangoImportWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }

    private void MangoApiTestButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<MangoApiTestWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }

    private void EmployeeStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<EmployeeStatisticsWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }

    private void CallLogButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<CallLogWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }

    private void TopicEnrichmentButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<TopicEnrichmentWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }

    private void GroupStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = App.AppHost.Services.GetRequiredService<GroupStatisticsWindow>();
        Hide();
        window.Owner = this;
        window.Show();
    }
}
