using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace CallCenterStatisticsApp.UI.Views;

public partial class JournalPage : UserControl
{
    private readonly AppDbContext _db;
    private readonly MangoCallImportService _importService;
    private readonly MangoCallTopicEnrichmentService _topicEnrichmentService;
    private readonly BusyService _busy;
    private bool _loaded;
    private bool _filtersLoaded;
    private bool _ready;
    private bool _suppressPeriodChange;
    private bool _isCustomPeriod;
    private DateTime _periodFrom;
    private DateTime _periodTo;

    public JournalPage(AppDbContext db, MangoCallImportService importService, MangoCallTopicEnrichmentService topicEnrichmentService, BusyService busy)
    {
        InitializeComponent();
        _db = db;
        _importService = importService;
        _topicEnrichmentService = topicEnrichmentService;
        _busy = busy;
        CallsDataGrid.MouseDoubleClick += CallsDataGrid_MouseDoubleClick;
        PeriodComboBox.SelectedValue = "Today";
        ApplyQuickPeriod("Today");
        _ready = true;
    }

    public async Task LoadTodayAsync()
    {
        await _topicEnrichmentService.RestoreTopicsFromImportedDataAsync();
        await LoadFiltersAsync();
        if (_loaded) return;
        await LoadDataAsync();
        _loaded = true;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPeriod(out var from, out var to)) return;

        try
        {
            using var operation = _busy.Begin("Синхронизация звонков с Mango…");
            SetBusy(true, "Синхронизация звонков с Mango…");
            await _importService.ImportCallsAsync(from, to.AddDays(1).AddSeconds(-1));
            await _topicEnrichmentService.RestoreTopicsFromImportedDataAsync();
            await LoadDataAsync();
            SummaryTextBlock.Text += " · синхронизировано с Mango";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось обновить журнал из Mango.\n\n{ex.Message}", "Ошибка синхронизации", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppressPeriodChange || PeriodComboBox.SelectedValue is not string period) return;

        if (period == "Custom")
        {
            _isCustomPeriod = true;
            PeriodHintTextBlock.Visibility = Visibility.Hidden;
            CustomPeriodPanel.Visibility = Visibility.Visible;
            return;
        }

        ApplyQuickPeriod(period);
        await LoadDataAsync();
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        DirectionComboBox.SelectedIndex = 0;
        DurationComboBox.SelectedIndex = 0;
        EmployeeComboBox.SelectedIndex = 0;
        GroupComboBox.SelectedIndex = 0;
        TopicComboBox.SelectedIndex = 0;
        SearchTextBox.Clear();

        _suppressPeriodChange = true;
        PeriodComboBox.SelectedValue = "Today";
        _suppressPeriodChange = false;
        ApplyQuickPeriod("Today");
        await LoadDataAsync();
    }

    private void ApplyQuickPeriod(string period)
    {
        var today = DateTime.Today;
        _periodFrom = period switch
        {
            "Yesterday" => today.AddDays(-1),
            "Week" => today.AddDays(-((int)today.DayOfWeek + 6) % 7),
            "SevenDays" => today.AddDays(-6),
            "Month" => new DateTime(today.Year, today.Month, 1),
            "ThirtyDays" => today.AddDays(-29),
            _ => today
        };
        _periodTo = today;
        _isCustomPeriod = false;
        FromDatePicker.SelectedDate = _periodFrom;
        ToDatePicker.SelectedDate = _periodTo;
        CustomPeriodPanel.Visibility = Visibility.Hidden;
        PeriodHintTextBlock.Visibility = Visibility.Visible;
        PeriodHintTextBlock.Text = $"{_periodFrom:dd.MM.yyyy} — {_periodTo:dd.MM.yyyy}";
    }

    private bool TryGetSelectedPeriod(out DateTime from, out DateTime to)
    {
        if (_isCustomPeriod)
        {
            if (FromDatePicker.SelectedDate is not DateTime customFrom || ToDatePicker.SelectedDate is not DateTime customTo)
            {
                from = to = default;
                MessageBox.Show("Укажите даты произвольного периода.", "Журнал звонков", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            from = customFrom.Date;
            to = customTo.Date;
            return from <= to;
        }

        from = _periodFrom.Date;
        to = _periodTo.Date;
        return true;
    }

    private async Task LoadDataAsync()
    {
        if (!TryGetSelectedPeriod(out var fromDate, out var toDate)) return;

        var from = fromDate.Date;
        var to = toDate.Date.AddDays(1);
        var direction = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var duration = (DurationComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var search = SearchTextBox.Text.Trim();
        int? employeeId = EmployeeComboBox.SelectedValue is int employee ? employee : null;
        int? groupId = GroupComboBox.SelectedValue is int group ? group : null;
        int? topicId = TopicComboBox.SelectedValue is int topic ? topic : null;

        var query = _db.CallRecords.AsNoTracking().Include(x => x.Employee).Include(x => x.Group).Include(x => x.Topic)
            .Where(x => x.CallDateTime >= from && x.CallDateTime < to);
        if (direction == "Входящие") query = query.Where(x => x.IsIncoming);
        if (direction == "Исходящие") query = query.Where(x => x.IsOutgoing);
        if (employeeId.HasValue) query = query.Where(x => x.EmployeeId == employeeId);
        if (groupId.HasValue) query = query.Where(x => x.GroupId == groupId);
        if (topicId.HasValue) query = query.Where(x => x.TopicId == topicId);
        if (duration == "До минуты") query = query.Where(x => x.DurationSeconds != null && x.DurationSeconds < 60);
        if (duration == "1–5 минут") query = query.Where(x => x.DurationSeconds >= 60 && x.DurationSeconds < 300);
        if (duration == "От 5 минут") query = query.Where(x => x.DurationSeconds >= 300);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => (x.ExternalPhoneNumber ?? "").Contains(search) || (x.Employee != null && x.Employee.FullName.Contains(search)) || (x.Group != null && x.Group.Name.Contains(search)));

        var rows = await query.OrderByDescending(x => x.CallDateTime).Select(x => new CallLogRow
        {
            Id = x.Id,
            CallDateTime = x.CallDateTime,
            EmployeeName = x.Employee == null ? "—" : x.Employee.FullName,
            GroupName = x.Group == null ? "—" : x.Group.Name,
            TopicName = x.Topic == null ? "—" : x.Topic.Name,
            ExternalPhoneNumber = x.ExternalPhoneNumber ?? "—",
            DirectionDisplay = x.IsIncoming ? "Входящий" : x.IsOutgoing ? "Исходящий" : "Внутренний",
            DurationDisplay = FormatDuration(x.DurationSeconds)
        }).ToListAsync();

        CallsDataGrid.ItemsSource = rows;
        SummaryTextBlock.Text = $"Записей: {rows.Count:N0}";
    }

    private static string FormatDuration(int? seconds) => seconds is not > 0 ? "—" : TimeSpan.FromSeconds(seconds.Value).ToString(@"m\:ss");

    private void SetBusy(bool isBusy, string? message)
    {
        Cursor = isBusy ? System.Windows.Input.Cursors.Wait : null;
        IsEnabled = !isBusy;
        if (message is not null) SummaryTextBlock.Text = message;
    }

    private async Task LoadFiltersAsync()
    {
        if (_filtersLoaded) return;
        EmployeeComboBox.ItemsSource = new[] { new FilterOption(null, "Все сотрудники") }.Concat(await _db.Employees.AsNoTracking().OrderBy(x => x.FullName).Select(x => new FilterOption(x.Id, x.FullName)).ToListAsync());
        GroupComboBox.ItemsSource = new[] { new FilterOption(null, "Все группы") }.Concat(await _db.CallGroups.AsNoTracking().OrderBy(x => x.Name).Select(x => new FilterOption(x.Id, x.Name)).ToListAsync());
        TopicComboBox.ItemsSource = new[] { new FilterOption(null, "Все тематики") }.Concat(await _db.CallTopics.AsNoTracking().OrderBy(x => x.Name).Select(x => new FilterOption(x.Id, x.Name)).ToListAsync());
        EmployeeComboBox.SelectedIndex = GroupComboBox.SelectedIndex = TopicComboBox.SelectedIndex = 0;
        _filtersLoaded = true;
    }

    private sealed record FilterOption(int? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private async void CallsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CallsDataGrid.SelectedItem is not CallLogRow row) return;
        var window = App.AppHost.Services.GetRequiredService<CallDetailsWindow>();
        window.Owner = Window.GetWindow(this);
        await window.LoadAsync(row.Id);
        window.ShowDialog();
    }

    private sealed class CallLogRow
    {
        public int Id { get; init; }
        public DateTime CallDateTime { get; init; }
        public string EmployeeName { get; init; } = "—";
        public string GroupName { get; init; } = "—";
        public string TopicName { get; init; } = "—";
        public string ExternalPhoneNumber { get; init; } = "—";
        public string DirectionDisplay { get; init; } = "—";
        public string DurationDisplay { get; init; } = "—";
    }
}
