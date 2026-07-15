using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Globalization;

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
    private TextBox _fromTimeTextBox = null!;
    private TextBox _toTimeTextBox = null!;
    private TextBlock _topicFilterText = null!;
    private Popup _topicFilterPopup = null!;
    private StackPanel _topicChoicesPanel = null!;
    private CheckBox _allTopicsCheckBox = null!;
    private CheckBox _withoutTopicsCheckBox = null!;
    private readonly List<(int Id, CheckBox CheckBox)> _topicCheckBoxes = new();
    private bool _suppressTopicSelectionChange;
    private MultiChoiceFilter _employeeFilter = null!;
    private MultiChoiceFilter _groupFilter = null!;
    private bool _suppressMultiChoiceSelectionChange;

    public JournalPage(AppDbContext db, MangoCallImportService importService, MangoCallTopicEnrichmentService topicEnrichmentService, BusyService busy)
    {
        InitializeComponent();
        _db = db;
        _importService = importService;
        _topicEnrichmentService = topicEnrichmentService;
        _busy = busy;
        ConfigureCustomTimeControls();
        _employeeFilter = ConfigureMultiChoiceFilter(EmployeeComboBox, "Все сотрудники", "Без сотрудников");
        _groupFilter = ConfigureMultiChoiceFilter(GroupComboBox, "Все группы", "Без групп");
        ConfigureTopicFilter();
        var journalTextStyle = new Style(typeof(TextBlock));
        journalTextStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0)));
        journalTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));

        foreach (var column in CallsDataGrid.Columns)
        {
            column.Width = DataGridLength.SizeToCells;
            if (column is DataGridTextColumn textColumn)
                textColumn.ElementStyle = journalTextStyle;
        }
        CallsDataGrid.CellStyle = (Style)Resources["JournalCellStyle"];
        CallsDataGrid.ColumnHeaderStyle = (Style)Resources["JournalHeaderStyle"];
        CallsDataGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
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
            await _importService.ImportCallsAsync(from, to);
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
        SelectAllMultiChoice(_employeeFilter);
        SelectAllMultiChoice(_groupFilter);
        SelectAllTopics();
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

    private void ConfigureCustomTimeControls()
    {
        CustomPeriodPanel.HorizontalAlignment = HorizontalAlignment.Left;
        PeriodHintTextBlock.HorizontalAlignment = HorizontalAlignment.Left;

        var headerGrid = (Grid)CustomPeriodPanel.Parent;
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock { Text = "От", Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _fromTimeTextBox = CreateTimeTextBox("00:00");
        panel.Children.Add(_fromTimeTextBox);
        panel.Children.Add(new TextBlock { Text = "До", Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        _toTimeTextBox = CreateTimeTextBox("23:59");
        panel.Children.Add(_toTimeTextBox);

        Grid.SetColumn(panel, 2);
        headerGrid.Children.Add(panel);
    }

    private static TextBox CreateTimeTextBox(string value) => new()
    {
        Width = 52,
        Height = 34,
        Text = value,
        MaxLength = 5,
        Padding = new Thickness(7, 0, 4, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234))
    };

    private static bool TryReadTime(TextBox input, string fieldName, out TimeSpan time)
    {
        if (DateTime.TryParseExact(input.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = parsed.TimeOfDay;
            return true;
        }

        time = default;
        MessageBox.Show($"Укажите {fieldName} в формате ЧЧ:ММ.", "Журнал звонков", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private MultiChoiceFilter ConfigureMultiChoiceFilter(ComboBox source, string allText, string withoutText)
    {
        source.Visibility = Visibility.Collapsed;
        var container = (StackPanel)source.Parent;
        var button = new Button
        {
            Width = 180,
            Height = 34,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };

        var filter = new MultiChoiceFilter
        {
            AllText = allText,
            WithoutText = withoutText,
            Caption = new TextBlock { Text = allText, Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)), VerticalAlignment = VerticalAlignment.Center },
            AllCheckBox = new CheckBox { Content = allText, Margin = new Thickness(4, 3, 4, 6) },
            WithoutCheckBox = new CheckBox { Content = withoutText, Margin = new Thickness(4, 3, 4, 8) },
            ChoicesPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }
        };
        button.Content = filter.Caption;
        container.Children.Add(button);

        var content = new StackPanel { Width = 250, Margin = new Thickness(10) };
        filter.AllCheckBox.Checked += (_, _) => AllMultiChoiceChanged(filter);
        filter.AllCheckBox.Unchecked += (_, _) => AllMultiChoiceChanged(filter);
        filter.WithoutCheckBox.Checked += (_, _) => WithoutMultiChoiceChanged(filter);
        filter.WithoutCheckBox.Unchecked += (_, _) => WithoutMultiChoiceChanged(filter);
        content.Children.Add(filter.AllCheckBox);
        content.Children.Add(filter.WithoutCheckBox);
        content.Children.Add(new Separator());
        content.Children.Add(new ScrollViewer { Content = filter.ChoicesPanel, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        filter.Popup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = content
            }
        };
        button.Click += (_, _) => filter.Popup.IsOpen = !filter.Popup.IsOpen;
        return filter;
    }

    private void ConfigureMultiChoiceOptions(MultiChoiceFilter filter, IEnumerable<FilterOption> options)
    {
        _suppressMultiChoiceSelectionChange = true;
        filter.ChoicesPanel.Children.Clear();
        filter.Choices.Clear();
        foreach (var option in options.Where(x => x.Id.HasValue))
        {
            var checkBox = new CheckBox { Content = option.Name, Margin = new Thickness(4, 3, 4, 3) };
            checkBox.Checked += (_, _) => MultiChoiceOptionChanged(filter);
            checkBox.Unchecked += (_, _) => MultiChoiceOptionChanged(filter);
            filter.ChoicesPanel.Children.Add(checkBox);
            filter.Choices.Add((option.Id!.Value, checkBox));
        }
        _suppressMultiChoiceSelectionChange = false;
        SelectAllMultiChoice(filter);
    }

    private void SelectAllMultiChoice(MultiChoiceFilter filter)
    {
        _suppressMultiChoiceSelectionChange = true;
        filter.WithoutCheckBox.IsChecked = false;
        filter.AllCheckBox.IsChecked = true;
        foreach (var (_, checkBox) in filter.Choices) checkBox.IsChecked = true;
        _suppressMultiChoiceSelectionChange = false;
        UpdateMultiChoiceCaption(filter);
    }

    private void AllMultiChoiceChanged(MultiChoiceFilter filter)
    {
        if (_suppressMultiChoiceSelectionChange) return;
        _suppressMultiChoiceSelectionChange = true;
        filter.WithoutCheckBox.IsChecked = false;
        var isChecked = filter.AllCheckBox.IsChecked == true;
        foreach (var (_, checkBox) in filter.Choices) checkBox.IsChecked = isChecked;
        _suppressMultiChoiceSelectionChange = false;
        UpdateMultiChoiceCaption(filter);
    }

    private void WithoutMultiChoiceChanged(MultiChoiceFilter filter)
    {
        if (_suppressMultiChoiceSelectionChange) return;
        _suppressMultiChoiceSelectionChange = true;
        if (filter.WithoutCheckBox.IsChecked == true)
        {
            filter.AllCheckBox.IsChecked = false;
            foreach (var (_, checkBox) in filter.Choices) checkBox.IsChecked = false;
        }
        _suppressMultiChoiceSelectionChange = false;
        UpdateMultiChoiceCaption(filter);
    }

    private void MultiChoiceOptionChanged(MultiChoiceFilter filter)
    {
        if (_suppressMultiChoiceSelectionChange) return;
        _suppressMultiChoiceSelectionChange = true;
        filter.WithoutCheckBox.IsChecked = false;
        filter.AllCheckBox.IsChecked = filter.Choices.Count > 0 && filter.Choices.All(x => x.CheckBox.IsChecked == true);
        _suppressMultiChoiceSelectionChange = false;
        UpdateMultiChoiceCaption(filter);
    }

    private static List<int> GetSelectedMultiChoiceIds(MultiChoiceFilter filter) => filter.Choices
        .Where(x => x.CheckBox.IsChecked == true)
        .Select(x => x.Id)
        .ToList();

    private static void UpdateMultiChoiceCaption(MultiChoiceFilter filter)
    {
        if (filter.WithoutCheckBox.IsChecked == true)
        {
            filter.Caption.Text = filter.WithoutText;
            return;
        }

        var selectedCount = filter.Choices.Count(x => x.CheckBox.IsChecked == true);
        filter.Caption.Text = selectedCount switch
        {
            0 => "Выберите значения",
            var count when count == filter.Choices.Count => filter.AllText,
            _ => $"Выбрано: {selectedCount}"
        };
    }

    private void ConfigureTopicFilter()
    {
        TopicComboBox.Visibility = Visibility.Collapsed;
        var container = (StackPanel)TopicComboBox.Parent;

        var button = new Button
        {
            Width = 180,
            Height = 34,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        _topicFilterText = new TextBlock { Text = "Все тематики", Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)), VerticalAlignment = VerticalAlignment.Center };
        button.Content = _topicFilterText;
        container.Children.Add(button);

        var content = new StackPanel { Width = 250, Margin = new Thickness(10) };
        _allTopicsCheckBox = new CheckBox { Content = "Все тематики", Margin = new Thickness(4, 3, 4, 6) };
        _withoutTopicsCheckBox = new CheckBox { Content = "Без тематик", Margin = new Thickness(4, 3, 4, 8) };
        _allTopicsCheckBox.Checked += AllTopicsCheckBox_Changed;
        _allTopicsCheckBox.Unchecked += AllTopicsCheckBox_Changed;
        _withoutTopicsCheckBox.Checked += WithoutTopicsCheckBox_Changed;
        _withoutTopicsCheckBox.Unchecked += WithoutTopicsCheckBox_Changed;
        content.Children.Add(_allTopicsCheckBox);
        content.Children.Add(_withoutTopicsCheckBox);
        content.Children.Add(new Separator());
        _topicChoicesPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        content.Children.Add(new ScrollViewer { Content = _topicChoicesPanel, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        _topicFilterPopup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(201, 215, 234)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = content
            }
        };
        button.Click += (_, _) => _topicFilterPopup.IsOpen = !_topicFilterPopup.IsOpen;
    }

    private void ConfigureTopicChoices(IEnumerable<FilterOption> topics)
    {
        _suppressTopicSelectionChange = true;
        _topicChoicesPanel.Children.Clear();
        _topicCheckBoxes.Clear();

        foreach (var topic in topics.Where(x => x.Id.HasValue))
        {
            var checkBox = new CheckBox { Content = topic.Name, Margin = new Thickness(4, 3, 4, 3) };
            checkBox.Checked += TopicCheckBox_Changed;
            checkBox.Unchecked += TopicCheckBox_Changed;
            _topicChoicesPanel.Children.Add(checkBox);
            _topicCheckBoxes.Add((topic.Id!.Value, checkBox));
        }

        _suppressTopicSelectionChange = false;
        SelectAllTopics();
    }

    private void SelectAllTopics()
    {
        if (_allTopicsCheckBox is null) return;

        _suppressTopicSelectionChange = true;
        _withoutTopicsCheckBox.IsChecked = false;
        _allTopicsCheckBox.IsChecked = true;
        foreach (var (_, checkBox) in _topicCheckBoxes) checkBox.IsChecked = true;
        _suppressTopicSelectionChange = false;
        UpdateTopicFilterText();
    }

    private void AllTopicsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTopicSelectionChange) return;

        _suppressTopicSelectionChange = true;
        _withoutTopicsCheckBox.IsChecked = false;
        var isChecked = _allTopicsCheckBox.IsChecked == true;
        foreach (var (_, checkBox) in _topicCheckBoxes) checkBox.IsChecked = isChecked;
        _suppressTopicSelectionChange = false;
        UpdateTopicFilterText();
    }

    private void WithoutTopicsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTopicSelectionChange) return;

        _suppressTopicSelectionChange = true;
        if (_withoutTopicsCheckBox.IsChecked == true)
        {
            _allTopicsCheckBox.IsChecked = false;
            foreach (var (_, checkBox) in _topicCheckBoxes) checkBox.IsChecked = false;
        }
        _suppressTopicSelectionChange = false;
        UpdateTopicFilterText();
    }

    private void TopicCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTopicSelectionChange) return;

        _suppressTopicSelectionChange = true;
        _withoutTopicsCheckBox.IsChecked = false;
        _allTopicsCheckBox.IsChecked = _topicCheckBoxes.Count > 0 && _topicCheckBoxes.All(x => x.CheckBox.IsChecked == true);
        _suppressTopicSelectionChange = false;
        UpdateTopicFilterText();
    }

    private void UpdateTopicFilterText()
    {
        if (_withoutTopicsCheckBox.IsChecked == true)
        {
            _topicFilterText.Text = "Без тематик";
            return;
        }

        var selectedCount = _topicCheckBoxes.Count(x => x.CheckBox.IsChecked == true);
        _topicFilterText.Text = selectedCount switch
        {
            0 => "Выберите тематики",
            var count when count == _topicCheckBoxes.Count => "Все тематики",
            _ => $"Тематик выбрано: {selectedCount}"
        };
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

            if (!TryReadTime(_fromTimeTextBox, "время «от»", out var fromTime) ||
                !TryReadTime(_toTimeTextBox, "время «до»", out var toTime))
            {
                from = to = default;
                return false;
            }

            from = customFrom.Date.Add(fromTime);
            to = customTo.Date.Add(toTime).AddSeconds(59);
            if (from <= to) return true;

            MessageBox.Show("Время окончания не может быть раньше времени начала.", "Журнал звонков", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!TryReadTime(_fromTimeTextBox, "время «от»", out var quickFromTime) ||
            !TryReadTime(_toTimeTextBox, "время «до»", out var quickToTime))
        {
            from = to = default;
            return false;
        }

        from = _periodFrom.Date.Add(quickFromTime);
        to = _periodTo.Date.Add(quickToTime).AddSeconds(59);
        if (from <= to) return true;

        MessageBox.Show("Время окончания не может быть раньше времени начала.", "Журнал звонков", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private async Task LoadDataAsync()
    {
        if (!TryGetSelectedPeriod(out var fromDate, out var toDate)) return;

        var from = fromDate;
        var to = toDate.AddSeconds(1);
        var direction = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var duration = (DurationComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var search = SearchTextBox.Text.Trim();
        var selectedEmployeeIds = GetSelectedMultiChoiceIds(_employeeFilter);
        var selectedGroupIds = GetSelectedMultiChoiceIds(_groupFilter);
        var showWithoutEmployees = _employeeFilter.WithoutCheckBox.IsChecked == true;
        var showWithoutGroups = _groupFilter.WithoutCheckBox.IsChecked == true;
        var selectedTopicIds = _topicCheckBoxes
            .Where(x => x.CheckBox.IsChecked == true)
            .Select(x => x.Id)
            .ToList();
        var showWithoutTopics = _withoutTopicsCheckBox.IsChecked == true;

        var query = _db.CallRecords.AsNoTracking().Include(x => x.Employee).Include(x => x.Group).Include(x => x.Topic)
            .Where(x => x.CallDateTime >= from && x.CallDateTime < to);
        if (direction == "Входящие") query = query.Where(x => x.IsIncoming);
        if (direction == "Исходящие") query = query.Where(x => x.IsOutgoing);
        if (showWithoutEmployees) query = query.Where(x => x.EmployeeId == null);
        else if (selectedEmployeeIds.Count > 0) query = query.Where(x => x.EmployeeId.HasValue && selectedEmployeeIds.Contains(x.EmployeeId.Value));
        if (showWithoutGroups) query = query.Where(x => x.GroupId == null);
        else if (selectedGroupIds.Count > 0) query = query.Where(x => x.GroupId.HasValue && selectedGroupIds.Contains(x.GroupId.Value));
        if (showWithoutTopics) query = query.Where(x => x.TopicId == null);
        else if (selectedTopicIds.Count > 0) query = query.Where(x => x.TopicId.HasValue && selectedTopicIds.Contains(x.TopicId.Value));
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
        var topics = await _db.CallTopics.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new FilterOption(x.Id, x.Name))
            .ToListAsync();
        ConfigureTopicChoices(topics);
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(x => x.FullName)
            .Select(x => new FilterOption(x.Id, x.FullName))
            .ToListAsync();
        var groups = await _db.CallGroups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new FilterOption(x.Id, x.Name))
            .ToListAsync();
        ConfigureMultiChoiceOptions(_employeeFilter, employees);
        ConfigureMultiChoiceOptions(_groupFilter, groups);
        EmployeeComboBox.SelectedIndex = GroupComboBox.SelectedIndex = 0;
        _filtersLoaded = true;
    }

    private sealed record FilterOption(int? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class MultiChoiceFilter
    {
        public string AllText { get; init; } = string.Empty;
        public string WithoutText { get; init; } = string.Empty;
        public TextBlock Caption { get; init; } = null!;
        public CheckBox AllCheckBox { get; init; } = null!;
        public CheckBox WithoutCheckBox { get; init; } = null!;
        public StackPanel ChoicesPanel { get; init; } = null!;
        public Popup Popup { get; set; } = null!;
        public List<(int Id, CheckBox CheckBox)> Choices { get; } = new();
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
