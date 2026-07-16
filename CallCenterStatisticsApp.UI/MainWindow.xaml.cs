using CallCenterStatisticsApp.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class MainWindow : Window
{
    private readonly DashboardPage _dashboardPage;
    private readonly JournalPage _journalPage;
    private readonly EmployeeStatisticsPage _employeeStatisticsPage;
    private readonly GoogleSheetsPage _googleSheetsPage;
    private readonly GroupStatisticsPage _groupStatisticsPage;
    private readonly ImportPage _importPage;
    private readonly TopicEnrichmentPage _topicEnrichmentPage;
    private readonly SettingsPage _settingsPage;
    private readonly BusyService _busy;

    public MainWindow(DashboardPage dashboardPage, JournalPage journalPage, EmployeeStatisticsPage employeeStatisticsPage, GoogleSheetsPage googleSheetsPage, GroupStatisticsPage groupStatisticsPage, ImportPage importPage, TopicEnrichmentPage topicEnrichmentPage, SettingsPage settingsPage, BusyService busy)
    {
        InitializeComponent();
        _dashboardPage = dashboardPage;
        _journalPage = journalPage;
        _employeeStatisticsPage = employeeStatisticsPage;
        _googleSheetsPage = googleSheetsPage;
        _groupStatisticsPage = groupStatisticsPage;
        _importPage = importPage;
        _topicEnrichmentPage = topicEnrichmentPage;
        _settingsPage = settingsPage;
        _busy = busy;
        _busy.Changed += Busy_Changed;
        ShowPage(_dashboardPage, "Обзор", "Готово к работе");
    }

    private void DashboardButton_Click(object sender, RoutedEventArgs e) => ShowPage(_dashboardPage, "Обзор", "Готово к работе");
    private async void JournalButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(_journalPage, "Журнал звонков", "Загружаем журнал звонков…");
        await _journalPage.LoadTodayAsync();
        StatusText.Text = "Журнал обновлён";
    }

    private void ShowPage(object page, string title, string status)
    {
        PageContent.Content = page;
        PageTitle.Text = title;
        StatusText.Text = status;
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e) => ShowPage(_importPage, "Импорт из Mango", "Выберите период для синхронизации");
    private void MangoApiTestButton_Click(object sender, RoutedEventArgs e) => ShowPage(_settingsPage, "Настройки", "Параметры подключения Mango");
    private async void EmployeeStatsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(_employeeStatisticsPage, "Статистика сотрудников", "Загружаем отчёт…");
        await _employeeStatisticsPage.LoadAsync();
        StatusText.Text = "Отчёт по сотрудникам готов";
    }
    private async void GroupStatsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(_groupStatisticsPage, "Статистика групп", "Загружаем отчёт…");
        await _groupStatisticsPage.LoadAsync();
        StatusText.Text = "Отчёт по группам готов";
    }
    private void TopicEnrichmentButton_Click(object sender, RoutedEventArgs e) => ShowPage(_topicEnrichmentPage, "Речевая аналитика", "Готово к обработке записей");

    private async void GoogleSheetsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(_googleSheetsPage, "Гугл-таблички", "Формируем рабочую таблицу…");
        await _googleSheetsPage.LoadAsync();
        StatusText.Text = "Таблица показателей готова";
    }

    private void OpenWindow<TWindow>() where TWindow : Window
    {
        var window = App.AppHost.Services.GetRequiredService<TWindow>();
        window.Owner = this;
        window.ShowDialog();
    }

    private void Busy_Changed(object? sender, BusyChangedEventArgs e)
    {
        BusyOverlay.Visibility = e.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyMessageText.Text = e.Message;
        if (e.IsBusy) StatusText.Text = e.Message;
        else StatusText.Text = "Готово";
    }
}
