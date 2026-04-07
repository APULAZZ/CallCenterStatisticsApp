using CallCenterStatisticsApp.Services;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace CallCenterStatisticsApp.UI;

public partial class MangoApiTestWindow : Window
{
    private readonly IMangoApiClient _mangoApiClient;

    public MangoApiTestWindow(IMangoApiClient mangoApiClient)
    {
        InitializeComponent();
        _mangoApiClient = mangoApiClient;

        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private async void LoadUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync("Загрузка сотрудников...", async () =>
        {
            var users = await _mangoApiClient.GetUsersAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"Сотрудников получено: {users.Count}");
            sb.AppendLine(new string('-', 80));

            foreach (var user in users)
            {
                sb.AppendLine($"Id: {user.Id}");
                sb.AppendLine($"Name: {user.Name}");
                sb.AppendLine($"Extension: {user.Extension}");
                sb.AppendLine(new string('-', 80));
            }

            OutputTextBox.Text = sb.ToString();
        });
    }

    private async void LoadUsersRawButton_Click(object sender, RoutedEventArgs e)
{
    await RunSafeAsync("Загрузка сырого JSON сотрудников...", async () =>
    {
        var raw = await _mangoApiClient.GetUsersRawAsync();
        OutputTextBox.Text = PrettyJsonOrRaw(raw);
    });
}

private async void LoadGroupsRawButton_Click(object sender, RoutedEventArgs e)
{
    await RunSafeAsync("Загрузка сырого JSON групп...", async () =>
    {
        var raw = await _mangoApiClient.GetGroupsRawAsync();
        OutputTextBox.Text = PrettyJsonOrRaw(raw);
    });
}

private async void LoadTopicsRawButton_Click(object sender, RoutedEventArgs e)
{
    await RunSafeAsync("Загрузка сырого JSON тематик...", async () =>
    {
        var raw = await _mangoApiClient.GetTopicsRawAsync();
        OutputTextBox.Text = PrettyJsonOrRaw(raw);
    });
}

private async void LoadCallsRawButton_Click(object sender, RoutedEventArgs e)
{
    if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
    {
        MessageBox.Show("Укажите даты периода.");
        return;
    }

    var from = FromDatePicker.SelectedDate.Value.Date;
    var to = ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

    await RunSafeAsync("Загрузка сырого JSON звонков...", async () =>
    {
        var raw = await _mangoApiClient.GetCallsRawAsync(from, to);
        OutputTextBox.Text = PrettyJsonOrRaw(raw);
    });
}

    private async void LoadGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync("Загрузка групп...", async () =>
        {
            var groups = await _mangoApiClient.GetGroupsAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"Групп получено: {groups.Count}");
            sb.AppendLine(new string('-', 80));

            foreach (var group in groups)
            {
                sb.AppendLine($"Id: {group.Id}");
                sb.AppendLine($"Name: {group.Name}");
                sb.AppendLine(new string('-', 80));
            }

            OutputTextBox.Text = sb.ToString();
        });
    }

    private async void LoadTopicsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync("Загрузка тематик...", async () =>
        {
            var topics = await _mangoApiClient.GetTopicsAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"Тематик получено: {topics.Count}");
            sb.AppendLine(new string('-', 80));

            foreach (var topic in topics)
            {
                sb.AppendLine($"Id: {topic.Id}");
                sb.AppendLine($"Name: {topic.Name}");
                sb.AppendLine(new string('-', 80));
            }

            OutputTextBox.Text = sb.ToString();
        });
    }

    private async void LoadCallsButton_Click(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
        {
            MessageBox.Show("Укажите даты периода.");
            return;
        }

        var from = FromDatePicker.SelectedDate.Value.Date;
        var to = ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        await RunSafeAsync("Загрузка звонков...", async () =>
        {
            var calls = await _mangoApiClient.GetCallsAsync(from, to);

            var sb = new StringBuilder();
            sb.AppendLine($"Звонков получено: {calls.Count}");
            sb.AppendLine($"Период: {from:yyyy-MM-dd HH:mm:ss} - {to:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 100));

            foreach (var call in calls.Take(100))
            {
                sb.AppendLine($"CallId: {call.CallId}");
                sb.AppendLine($"CallDateTime: {call.CallDateTime}");
                sb.AppendLine($"Direction: {call.Direction}");
                sb.AppendLine($"EmployeeMangoId: {call.EmployeeMangoId}");
                sb.AppendLine($"GroupMangoId: {call.GroupMangoId}");
                sb.AppendLine($"TopicMangoId: {call.TopicMangoId}");
                sb.AppendLine($"PhoneNumber: {call.PhoneNumber}");
                sb.AppendLine($"StatusCode: {call.StatusCode}");
                sb.AppendLine($"StatusText: {call.StatusText}");
                sb.AppendLine($"DurationSeconds: {call.DurationSeconds}");
                sb.AppendLine($"TalkDurationSeconds: {call.TalkDurationSeconds}");
                sb.AppendLine($"WaitDurationSeconds: {call.WaitDurationSeconds}");
                sb.AppendLine("RawJson:");
                sb.AppendLine(PrettyJsonOrRaw(call.RawJson));
                sb.AppendLine(new string('-', 100));
            }

            if (calls.Count > 100)
            {
                sb.AppendLine();
                sb.AppendLine($"Показаны только первые 100 записей из {calls.Count}.");
            }

            OutputTextBox.Text = sb.ToString();
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        OutputTextBox.Clear();
        StatusTextBlock.Text = "Очищено.";
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

    private async Task RunSafeAsync(string status, Func<Task> action)
    {
        try
        {
            StatusTextBlock.Text = status;
            Mouse.OverrideCursor = Cursors.Wait;

            await action();

            StatusTextBlock.Text = "Готово.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Ошибка.";
            OutputTextBox.Text = ex.ToString();
            MessageBox.Show(ex.Message, "Ошибка MANGO API");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static string PrettyJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return json;
        }
    }
}