using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace CallCenterStatisticsApp.UI.Views;

public partial class EmployeeStatisticsPage : UserControl
{
    private readonly AppDbContext _db;
    private readonly CallStatisticsService _statistics;
    private readonly MangoCallImportService _callImport;
    private readonly BusyService _busy;
    private MultiChoiceFilter _employeeFilter = null!;
    private MultiChoiceFilter _groupFilter = null!;
    private MultiChoiceFilter _topicFilter = null!;
    private bool _suppressFilterChanges;
    private bool _loaded;
    private bool _filtersLoaded;
    private bool _ready;
    private DateTime _periodFrom;
    private DateTime _periodTo;
    private List<(int Id, string Name)> _availableTopics = [];
    private List<EmployeeStatRow> _reportRows = [];
    private string? _sortMember;
    private ListSortDirection _sortDirection;

    public EmployeeStatisticsPage(AppDbContext db, CallStatisticsService statistics, MangoCallImportService callImport, BusyService busy)
    {
        InitializeComponent();
        _db = db;
        _statistics = statistics;
        _callImport = callImport;
        _busy = busy;
        _employeeFilter = ConfigureMultiChoiceFilter(EmployeeComboBox, "Все сотрудники", "Без сотрудников");
        _groupFilter = ConfigureMultiChoiceFilter(GroupComboBox, "Все группы", "Без групп");
        _topicFilter = ConfigureMultiChoiceFilter(TopicComboBox, "Все тематики", "Без тематик");
        ConfigureIgnoreTopicsOption(_topicFilter);
        ConfigureCombineTopicsOption(_topicFilter);

        foreach (var column in StatsDataGrid.Columns) column.Width = DataGridLength.SizeToHeader;
        StatsDataGrid.Columns[0].Width = DataGridLength.SizeToCells;
        StatsDataGrid.Sorting += StatsDataGrid_Sorting;
        PeriodComboBox.SelectedValue = "Today";
        ApplyQuickPeriod("Today");
        _ready = true;
    }

    public async Task LoadAsync()
    {
        if (!_filtersLoaded) await LoadFiltersAsync();
        if (_loaded) return;
        await LoadDataAsync();
        _loaded = true;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_filtersLoaded) await LoadFiltersAsync();
        await LoadDataAsync(synchronizeWithMango: true);
    }

    private async void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || PeriodComboBox.SelectedValue is not string period) return;

        if (period == "Custom")
        {
            PeriodHintTextBlock.Visibility = Visibility.Collapsed;
            CustomPeriodPanel.Visibility = Visibility.Visible;
            return;
        }

        ApplyQuickPeriod(period);
        if (_loaded) await LoadDataAsync();
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
        _periodTo = period == "Yesterday" ? today.AddDays(-1) : today;
        FromDatePicker.SelectedDate = _periodFrom;
        ToDatePicker.SelectedDate = _periodTo;
        CustomPeriodPanel.Visibility = Visibility.Collapsed;
        PeriodHintTextBlock.Visibility = Visibility.Visible;
        PeriodHintTextBlock.Text = $"{_periodFrom:dd.MM.yyyy} — {_periodTo:dd.MM.yyyy}";
    }

    private async Task LoadFiltersAsync()
    {
        var employees = await _db.Employees.AsNoTracking().OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName }).ToListAsync();
        ConfigureMultiChoiceOptions(_employeeFilter, employees
            .GroupBy(x => x.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => new FilterOption(x.Select(y => y.Id).ToList(), x.First().FullName)));
        var groups = await _db.CallGroups.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
        ConfigureMultiChoiceOptions(_groupFilter, groups.Select(x => new FilterOption([x.Id], x.Name)));
        SelectDefaultGroup("Коллцентр");
        await ConfigureEmployeeOptionsAsync();
        var topics = await _db.CallTopics.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
        _availableTopics = topics.Select(x => (x.Id, x.Name)).ToList();
        ConfigureTopicOptions();
        _filtersLoaded = true;
    }

    private async Task LoadDataAsync(bool synchronizeWithMango = false)
    {
        if (!TryGetPeriod(out var from, out var to)) return;

        using var operation = _busy.Begin("Формируем статистику сотрудников…");
        if (synchronizeWithMango)
            await _callImport.EnsurePeriodImportedAsync(from, to);

        var filter = new EmployeeStatisticsFilter
        {
            // "All employees" means no additional restriction. This is
            // important after MANGO has created a technical duplicate while
            // refreshing the current day: it must still be included by group.
            EmployeeIds = _employeeFilter.AllCheckBox.IsChecked == true ? null : GetSelectedIds(_employeeFilter),
            WithoutEmployees = _employeeFilter.WithoutCheckBox.IsChecked == true,
            GroupIds = GetSelectedIds(_groupFilter),
            WithoutGroups = _groupFilter.WithoutCheckBox.IsChecked == true,
            LimitEmployeesToGroups = _groupFilter.AllCheckBox.IsChecked != true && _groupFilter.WithoutCheckBox.IsChecked != true,
            TopicIds = GetSelectedIds(_topicFilter),
            WithoutTopics = _topicFilter.WithoutCheckBox.IsChecked == true,
            IgnoreTopics = _topicFilter.IgnoreCheckBox?.IsChecked == true
        };
        var rows = await _statistics.GetEmployeeStatsAsync(from, to, filter);
        _reportRows = rows;
        DisplayRows();
        SummaryTextBlock.Text = $"Сотрудников в отчёте: {rows.Count(x => !x.IsTotal):N0}";
    }

    private void StatsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var sortMember = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortMember) && e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            sortMember = binding.Path?.Path;
        if (string.IsNullOrWhiteSpace(sortMember)) return;

        e.Handled = true;
        _sortDirection = string.Equals(_sortMember, sortMember, StringComparison.Ordinal)
            && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortMember = sortMember;

        foreach (var column in StatsDataGrid.Columns)
            column.SortDirection = null;
        e.Column.SortDirection = _sortDirection;
        DisplayRows();
    }

    private void DisplayRows()
    {
        var total = _reportRows.FirstOrDefault(x => x.IsTotal);
        IEnumerable<EmployeeStatRow> rows = _reportRows.Where(x => !x.IsTotal);

        if (!string.IsNullOrWhiteSpace(_sortMember))
        {
            var property = typeof(EmployeeStatRow).GetProperty(_sortMember);
            if (property is not null)
            {
                rows = _sortDirection == ListSortDirection.Ascending
                    ? rows.OrderBy(x => property.GetValue(x))
                    : rows.OrderByDescending(x => property.GetValue(x));
            }
        }

        StatsDataGrid.ItemsSource = total is null ? rows.ToList() : rows.Append(total).ToList();
    }

    private MultiChoiceFilter ConfigureMultiChoiceFilter(ComboBox comboBox, string allText, string withoutText)
    {
        comboBox.Visibility = Visibility.Collapsed;
        var container = (StackPanel)comboBox.Parent;
        var caption = new TextBlock { Text = allText, Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)), VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Width = comboBox.Width,
            Height = comboBox.Height,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = caption
        };
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var content = new StackPanel { Margin = new Thickness(8) };
        var allCheckBox = new CheckBox { Content = allText, Margin = new Thickness(4, 3, 4, 6) };
        var withoutCheckBox = new CheckBox { Content = withoutText, Margin = new Thickness(4, 3, 4, 8) };
        var choicesPanel = new StackPanel();
        content.Children.Add(allCheckBox);
        content.Children.Add(withoutCheckBox);
        content.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 5) });
        content.Children.Add(new ScrollViewer { Content = choicesPanel, MaxHeight = 310, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinWidth = comboBox.Width });
        popup.Child = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = content };
        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        var filter = new MultiChoiceFilter(allText, withoutText, caption, allCheckBox, withoutCheckBox, choicesPanel, content);
        allCheckBox.Checked += (_, _) => AllCheckBoxChanged(filter);
        allCheckBox.Unchecked += (_, _) => AllCheckBoxChanged(filter);
        withoutCheckBox.Checked += (_, _) => WithoutCheckBoxChanged(filter);
        withoutCheckBox.Unchecked += (_, _) => WithoutCheckBoxChanged(filter);
        container.Children.Add(button);
        container.Children.Add(popup);
        return filter;
    }

    private void ConfigureIgnoreTopicsOption(MultiChoiceFilter filter)
    {
        var ignoreCheckBox = new CheckBox { Content = "Без учёта тематик", Margin = new Thickness(4, 3, 4, 8) };
        ignoreCheckBox.Checked += (_, _) => IgnoreTopicsCheckBoxChanged(filter);
        ignoreCheckBox.Unchecked += (_, _) => IgnoreTopicsCheckBoxChanged(filter);
        filter.ContentPanel.Children.Insert(2, ignoreCheckBox);
        filter.IgnoreCheckBox = ignoreCheckBox;
    }

    private void ConfigureCombineTopicsOption(MultiChoiceFilter filter)
    {
        var combineCheckBox = new CheckBox { Content = "Совмещать «вх.» и «исх.»", Margin = new Thickness(4, 3, 4, 8), IsChecked = true };
        combineCheckBox.Checked += (_, _) => CombineTopicsCheckBoxChanged();
        combineCheckBox.Unchecked += (_, _) => CombineTopicsCheckBoxChanged();
        filter.ContentPanel.Children.Insert(3, combineCheckBox);
        filter.CombineCheckBox = combineCheckBox;
    }

    private void CombineTopicsCheckBoxChanged()
    {
        if (_suppressFilterChanges || !_filtersLoaded) return;
        ConfigureTopicOptions();
    }

    private void ConfigureTopicOptions()
    {
        var options = _topicFilter.CombineCheckBox?.IsChecked == true
            ? _availableTopics.GroupBy(x => CallCenterTopicCatalog.GetDisplayName(x.Name), StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new FilterOption(x.Select(y => y.Id).ToList(), x.Key))
            : _availableTopics.Select(x => new FilterOption([x.Id], x.Name));
        ConfigureMultiChoiceOptions(_topicFilter, options);
    }


    private void ConfigureMultiChoiceOptions(MultiChoiceFilter filter, IEnumerable<FilterOption> options)
    {
        _suppressFilterChanges = true;
        filter.ChoicesPanel.Children.Clear();
        filter.Choices.Clear();
        foreach (var option in options)
        {
            var checkBox = new CheckBox { Content = option.Name, Margin = new Thickness(4, 3, 4, 3), IsChecked = true };
            checkBox.Checked += (_, _) => ChoiceCheckBoxChanged(filter, option.Name);
            checkBox.Unchecked += (_, _) => ChoiceCheckBoxChanged(filter, option.Name);
            filter.ChoicesPanel.Children.Add(checkBox);
            filter.Choices.Add((option.Ids, option.Name, checkBox));
        }
        filter.AllCheckBox.IsChecked = true;
        filter.WithoutCheckBox.IsChecked = false;
        if (filter.IgnoreCheckBox != null) filter.IgnoreCheckBox.IsChecked = false;
        _suppressFilterChanges = false;
        UpdateFilterCaption(filter);
    }

    private void AllCheckBoxChanged(MultiChoiceFilter filter)
    {
        if (_suppressFilterChanges) return;
        _suppressFilterChanges = true;
        filter.WithoutCheckBox.IsChecked = false;
        if (filter.IgnoreCheckBox != null) filter.IgnoreCheckBox.IsChecked = false;
        var isChecked = filter.AllCheckBox.IsChecked == true;
        foreach (var choice in filter.Choices) choice.CheckBox.IsChecked = isChecked;
        _suppressFilterChanges = false;
        UpdateFilterCaption(filter);
        if (ReferenceEquals(filter, _groupFilter) && _filtersLoaded)
            _ = ConfigureEmployeeOptionsAsync();
    }

    private void WithoutCheckBoxChanged(MultiChoiceFilter filter)
    {
        if (_suppressFilterChanges) return;
        _suppressFilterChanges = true;
        if (filter.IgnoreCheckBox != null) filter.IgnoreCheckBox.IsChecked = false;
        if (filter.WithoutCheckBox.IsChecked == true)
        {
            filter.AllCheckBox.IsChecked = false;
            foreach (var choice in filter.Choices) choice.CheckBox.IsChecked = false;
        }
        _suppressFilterChanges = false;
        UpdateFilterCaption(filter);
        if (ReferenceEquals(filter, _groupFilter) && _filtersLoaded)
            _ = ConfigureEmployeeOptionsAsync();
    }

    private void ChoiceCheckBoxChanged(MultiChoiceFilter filter, string changedTopicName)
    {
        if (_suppressFilterChanges) return;
        _suppressFilterChanges = true;
        filter.WithoutCheckBox.IsChecked = false;
        if (filter.IgnoreCheckBox != null) filter.IgnoreCheckBox.IsChecked = false;

        if (ReferenceEquals(filter, _topicFilter))
            SynchronizeTopicGroupSelection(filter, changedTopicName);

        filter.AllCheckBox.IsChecked = filter.Choices.Count > 0 && filter.Choices.All(x => x.CheckBox.IsChecked == true);
        _suppressFilterChanges = false;
        UpdateFilterCaption(filter);
        if (ReferenceEquals(filter, _groupFilter) && _filtersLoaded)
            _ = ConfigureEmployeeOptionsAsync();
    }

    private async Task ConfigureEmployeeOptionsAsync()
    {
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(x => x.FullName)
            .Select(x => new { x.Id, x.FullName })
            .ToListAsync();

        var selectedGroupIds = GetSelectedIds(_groupFilter);
        var groupFilterIsActive = _groupFilter.AllCheckBox.IsChecked != true &&
                                  _groupFilter.WithoutCheckBox.IsChecked != true;
        if (groupFilterIsActive && selectedGroupIds is { Count: > 0 })
        {
            var isCallCenterSelected = await _db.CallGroups.AsNoTracking().AnyAsync(x =>
                selectedGroupIds.Contains(x.Id) && x.Name == "\u041a\u043e\u043b\u043b\u0446\u0435\u043d\u0442\u0440");
            if (isCallCenterSelected)
            {
                employees = employees.Where(x =>
                    x.FullName.StartsWith("\u041a\u0426 ", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.FullName, "\u0417\u043e\u044f \u0415\u0440\u0448\u043e\u0432\u0430", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        ConfigureMultiChoiceOptions(_employeeFilter, employees
            .GroupBy(x => x.FullName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => new FilterOption(x.Select(y => y.Id).ToList(), x.First().FullName)));
    }

    private static void SynchronizeTopicGroupSelection(MultiChoiceFilter filter, string changedTopicName)
    {
        var parentName = GetTopicGroupName(changedTopicName);
        if (parentName is null) return;

        var parent = filter.Choices.FirstOrDefault(x => string.Equals(x.Name, parentName, StringComparison.OrdinalIgnoreCase));
        if (parent.CheckBox is null) return;

        var children = filter.Choices.Where(x => IsTopicClinicVariant(x.Name, parentName)).ToList();
        if (children.Count == 0) return;

        if (string.Equals(changedTopicName, parentName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var child in children)
                child.CheckBox.IsChecked = parent.CheckBox.IsChecked == true;
        }
        else
        {
            parent.CheckBox.IsChecked = children.All(x => x.CheckBox.IsChecked == true);
        }
    }

    private static string? GetTopicGroupName(string topicName)
    {
        if (string.Equals(topicName, "ПЛАН", StringComparison.OrdinalIgnoreCase) || IsTopicClinicVariant(topicName, "ПЛАН")) return "ПЛАН";
        if (string.Equals(topicName, "ПЕРК", StringComparison.OrdinalIgnoreCase) || IsTopicClinicVariant(topicName, "ПЕРК")) return "ПЕРК";
        return null;
    }

    private static bool IsTopicClinicVariant(string topicName, string groupName)
    {
        var name = topicName.Trim();
        return name.StartsWith(groupName + " ", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith(groupName + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<int>? GetSelectedIds(MultiChoiceFilter filter)
        => filter.WithoutCheckBox.IsChecked == true || filter.IgnoreCheckBox?.IsChecked == true
            ? null
            : filter.Choices.Where(x => x.CheckBox.IsChecked == true).SelectMany(x => x.Ids).Distinct().ToList();

    private static void UpdateFilterCaption(MultiChoiceFilter filter)
    {
        if (filter.IgnoreCheckBox?.IsChecked == true) filter.Caption.Text = "Без учёта тематик";
        else if (filter.WithoutCheckBox.IsChecked == true) filter.Caption.Text = filter.WithoutText;
        else if (filter.AllCheckBox.IsChecked == true) filter.Caption.Text = filter.AllText;
        else
        {
            var selected = filter.Choices.Where(x => x.CheckBox.IsChecked == true).Select(x => x.CheckBox.Content?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            filter.Caption.Text = selected.Count switch { 0 => "Не выбрано", 1 => selected[0]!, _ => $"Выбрано: {selected.Count}" };
        }
    }

    private void IgnoreTopicsCheckBoxChanged(MultiChoiceFilter filter)
    {
        if (_suppressFilterChanges) return;
        _suppressFilterChanges = true;
        if (filter.IgnoreCheckBox?.IsChecked == true)
        {
            filter.AllCheckBox.IsChecked = false;
            filter.WithoutCheckBox.IsChecked = false;
            foreach (var choice in filter.Choices) choice.CheckBox.IsChecked = false;
        }
        _suppressFilterChanges = false;
        UpdateFilterCaption(filter);
    }

    private void SelectDefaultGroup(string groupName)
    {
        var group = _groupFilter.Choices.FirstOrDefault(x => string.Equals(x.CheckBox.Content?.ToString(), groupName, StringComparison.OrdinalIgnoreCase));
        if (group.CheckBox is null) return;
        _suppressFilterChanges = true;
        _groupFilter.AllCheckBox.IsChecked = false;
        _groupFilter.WithoutCheckBox.IsChecked = false;
        foreach (var choice in _groupFilter.Choices) choice.CheckBox.IsChecked = choice.Ids.SequenceEqual(group.Ids);
        _suppressFilterChanges = false;
        UpdateFilterCaption(_groupFilter);
    }

    private bool TryGetPeriod(out DateTime from, out DateTime to)
    {
        from = default;
        to = default;
        if (FromDatePicker.SelectedDate is not DateTime fromDate || ToDatePicker.SelectedDate is not DateTime toDate)
        {
            MessageBox.Show("Выберите дату начала и окончания периода.", "Статистика сотрудников", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!TimeOnly.TryParse(FromTimeTextBox.Text, out var fromTime) || !TimeOnly.TryParse(ToTimeTextBox.Text, out var toTime))
        {
            MessageBox.Show("Укажите время в формате ЧЧ:ММ.", "Статистика сотрудников", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        from = fromDate.Date.Add(fromTime.ToTimeSpan());
        to = toDate.Date.Add(toTime.ToTimeSpan()).AddSeconds(59);
        if (to < from)
        {
            MessageBox.Show("Окончание периода не может быть раньше начала.", "Статистика сотрудников", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private sealed record FilterOption(IReadOnlyCollection<int> Ids, string Name);

    private sealed class MultiChoiceFilter
    {
        public MultiChoiceFilter(string allText, string withoutText, TextBlock caption, CheckBox allCheckBox, CheckBox withoutCheckBox, StackPanel choicesPanel, StackPanel contentPanel)
        {
            AllText = allText;
            WithoutText = withoutText;
            Caption = caption;
            AllCheckBox = allCheckBox;
            WithoutCheckBox = withoutCheckBox;
            ChoicesPanel = choicesPanel;
            ContentPanel = contentPanel;
        }
        public string AllText { get; }
        public string WithoutText { get; }
        public TextBlock Caption { get; }
        public CheckBox AllCheckBox { get; }
        public CheckBox WithoutCheckBox { get; }
        public StackPanel ChoicesPanel { get; }
        public StackPanel ContentPanel { get; }
        public CheckBox? IgnoreCheckBox { get; set; }
        public CheckBox? CombineCheckBox { get; set; }
        public List<(IReadOnlyCollection<int> Ids, string Name, CheckBox CheckBox)> Choices { get; } = [];
    }
}
