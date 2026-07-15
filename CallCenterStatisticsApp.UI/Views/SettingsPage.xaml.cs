using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using System.Windows;
using System.Windows.Controls;
namespace CallCenterStatisticsApp.UI.Views;
public partial class SettingsPage : UserControl
{
    private readonly MangoApiOptions _options; private readonly IMangoApiClient _api; private readonly ThemeService _theme; private readonly BusyService _busy;
    public SettingsPage(MangoApiOptions options, IMangoApiClient api, ThemeService theme, BusyService busy) { InitializeComponent(); _options = options; _api = api; _theme = theme; _busy = busy; EndpointTextBlock.Text = $"Адрес: {_options.BaseUrl}"; CredentialsTextBlock.Text = string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSalt) ? "Ключи API не настроены." : "Ключи API настроены."; }
    private async void TestButton_Click(object sender, RoutedEventArgs e) { try { using var operation = _busy.Begin("Проверяем подключение к Mango…"); TestButton.IsEnabled = false; StatusTextBlock.Text = "Проверяем подключение…"; var users = await _api.GetUsersAsync(); StatusTextBlock.Text = $"Подключение работает. Получено сотрудников: {users.Count}."; } catch (Exception ex) { StatusTextBlock.Text = $"Не удалось подключиться: {ex.Message}"; } finally { TestButton.IsEnabled = true; } }
    private void ThemeButton_Click(object sender, RoutedEventArgs e) { _theme.Toggle(); ThemeButton.Content = _theme.IsDark ? "Включить светлую тему" : "Включить тёмную тему"; }
}
