using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace CallCenterStatisticsApp.UI.Views;

public partial class GoogleSheetsPage : UserControl
{
    private readonly AppDbContext _db;
    private readonly MangoCallImportService _callImport;
    private readonly BusyService _busy;
    private readonly AnnualSummaryPage _annualSummaryPage;
    private bool _loaded;
    private List<ManualEntry> _manualEntries = [];

    public GoogleSheetsPage(AppDbContext db, MangoCallImportService callImport, BusyService busy, AnnualSummaryPage annualSummaryPage)
    {
        InitializeComponent();
        _db = db;
        _callImport = callImport;
        _busy = busy;
        _annualSummaryPage = annualSummaryPage;
        AnnualSummaryHost.Content = _annualSummaryPage;
        MonthComboBox.ItemsSource = Enumerable.Range(1, 12).Select(x => new MonthOption(x, new DateTime(2000, x, 1).ToString("MMMM"))).ToList();
        MonthComboBox.DisplayMemberPath = nameof(MonthOption.Name);
        MonthComboBox.SelectedValuePath = nameof(MonthOption.Number);
        YearComboBox.ItemsSource = Enumerable.Range(DateTime.Today.Year - 2, 5).ToList();
        MonthComboBox.SelectedValue = DateTime.Today.Month;
        YearComboBox.SelectedItem = DateTime.Today.Year;
    }

    public async Task LoadAsync()
    {
        if (!_loaded)
        {
            var names = await _db.Employees.AsNoTracking().Select(x => x.FullName).ToListAsync();
            var employees = names.Where(IsCallCenterEmployee).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            employees.Insert(0, "Все сотрудники");
            EmployeeComboBox.ItemsSource = employees;
            EmployeeComboBox.SelectedIndex = 0;
            ManualEmployeeComboBox.ItemsSource = employees.Skip(1).ToList();
            ManualTypeComboBox.ItemsSource = new[] { "График админов КЦ", "Норма часов", "Отпуск", "Отгул" };
            ManualTypeComboBox.SelectedIndex = 0;
            ManualDatePicker.SelectedDate = DateTime.Today;
            var setting = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == "ManualCallCenterTables");
            _manualEntries = setting is null ? [] : JsonSerializer.Deserialize<List<ManualEntry>>(setting.Value) ?? [];
            BuildManualTable();
            _loaded = true;
        }
        await LoadRowsAsync();
        await _annualSummaryPage.LoadAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedMonth(out var from, out var to)) return;
        using var operation = _busy.Begin("Синхронизируем звонки и рассчитываем таблицу…");
        await _callImport.EnsurePeriodImportedAsync(from, to);
        await LoadRowsAsync();
    }
    private async void AddManualEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (ManualDatePicker.SelectedDate is not DateTime date || ManualTypeComboBox.SelectedItem is not string type || ManualEmployeeComboBox.SelectedItem is not string employee) return;
        _manualEntries.Add(new ManualEntry { Date = date.Date, Type = type, EmployeeName = employee, Value = ManualValueTextBox.Text.Trim() });
        var setting = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == "ManualCallCenterTables");
        if (setting is null) { setting = new CallCenterStatisticsApp.Models.AppSetting { Key = "ManualCallCenterTables" }; _db.AppSettings.Add(setting); }
        setting.Value = JsonSerializer.Serialize(_manualEntries);
        await _db.SaveChangesAsync();
        ManualValueTextBox.Clear();
        BuildManualTable();
    }
    private async void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loaded) await LoadRowsAsync(); }
    private async void EmployeeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_loaded) await LoadRowsAsync(); }

    private async Task LoadRowsAsync()
    {
        if (!TryGetSelectedMonth(out var from, out var to)) return;
        var employee = EmployeeComboBox.SelectedItem as string;
        var topics = await _db.CallTopics.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var calls = await _db.CallRecords.AsNoTracking().Include(x => x.Employee)
            .Where(x => x.CallDateTime >= from && x.CallDateTime < to && x.EmployeeId.HasValue)
            .ToListAsync();
        var rows = calls.Where(x => x.Employee != null && IsCallCenterEmployee(x.Employee.FullName) &&
                                    (employee == "Все сотрудники" || string.Equals(x.Employee.FullName, employee, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(x => new { Day = x.CallDateTime.Date, x.Employee!.FullName })
            .Select(g => CreateRow(g.Key.Day, g.Key.FullName, g.ToList(), topics))
            .OrderBy(x => x.Date).ThenBy(x => x.EmployeeName).ToList();
        BuildDailyTable(rows);
        var clinicRows = calls.Where(x => x.Employee != null && IsCallCenterEmployee(x.Employee.FullName) &&
                                    (employee == "Все сотрудники" || string.Equals(x.Employee.FullName, employee, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new { Call = x, Topic = TopicName(x, topics) })
            .Where(x => CallCenterTopicCatalog.IsPerkOrPlan(x.Topic) && CallCenterTopicCatalog.TryGetClinic(x.Topic, out _))
            .GroupBy(x => new { Day = x.Call.CallDateTime.Date, x.Call.Employee!.FullName, Clinic = GetClinicName(x.Topic) })
            .Select(g => new ClinicTableRow
            {
                Date = g.Key.Day,
                EmployeeName = g.Key.FullName,
                Clinic = g.Key.Clinic,
                Perk = g.Count(x => CallCenterTopicCatalog.IsPerk(x.Topic)),
                Plan = g.Count(x => CallCenterTopicCatalog.IsPlan(x.Topic))
            })
            .OrderBy(x => x.Date).ThenBy(x => x.EmployeeName).ThenBy(x => x.Clinic).ToList();
        BuildClinicTable(clinicRows);
    }

    private static DailyTableRow CreateRow(DateTime date, string employee, IReadOnlyCollection<CallCenterStatisticsApp.Models.CallRecord> calls, IReadOnlyDictionary<int, string> topics)
    {
        string TopicName(CallCenterStatisticsApp.Models.CallRecord call) => call.TopicId.HasValue && topics.TryGetValue(call.TopicId.Value, out var name) ? name : string.Empty;
        var incoming = calls.Count(x => x.IsIncoming && x.IsAnswered);
        var outgoing = calls.Count(x => x.IsOutgoing);
        var perk = calls.Count(x => CallCenterTopicCatalog.IsPerk(TopicName(x)));
        var plan = calls.Count(x => CallCenterTopicCatalog.IsPlan(TopicName(x)));
        var noAppointment = calls.Count(x => CallCenterTopicCatalog.IsNoAppointment(TopicName(x)));
        var drop = calls.Count(x => CallCenterTopicCatalog.IsDrop(TopicName(x)));
        var transfers = calls.Count(x => x.RawJson?.Contains("\"BlindTransfer\":true", StringComparison.OrdinalIgnoreCase) == true || x.RawJson?.Contains("\"ConsultTransfer\":true", StringComparison.OrdinalIgnoreCase) == true);
        var denominator = incoming + outgoing;
        return new DailyTableRow { Date = date, EmployeeName = employee, Incoming = incoming, Outgoing = outgoing, Perk = perk, Plan = plan, NoAppointment = noAppointment, Drop = drop, Transfers = transfers, AppointmentPercent = denominator == 0 ? 0 : 100d * (perk + plan) / denominator, NoAppointmentPercent = denominator == 0 ? 0 : 100d * noAppointment / denominator };
    }

    private static string TopicName(CallCenterStatisticsApp.Models.CallRecord call, IReadOnlyDictionary<int, string> topics)
        => call.TopicId.HasValue && topics.TryGetValue(call.TopicId.Value, out var name) ? name : string.Empty;

    private static DataTable BuildClinicMatrix(IEnumerable<ClinicTableRow> source)
    {
        var rows = source.ToList();
        var clinics = rows.Select(x => x.Clinic).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var table = new DataTable();
        table.Columns.Add("Дата");
        table.Columns.Add("Сотрудник");
        foreach (var clinic in clinics)
        {
            table.Columns.Add($"{clinic}\nПЕРК");
            table.Columns.Add($"{clinic}\nПЛАН");
        }
        table.Columns.Add("Итого\nПЕРК");
        table.Columns.Add("Итого\nПЛАН");
        foreach (var day in rows.GroupBy(x => new { x.Date, x.EmployeeName }).OrderBy(x => x.Key.Date).ThenBy(x => x.Key.EmployeeName))
        {
            var row = table.NewRow();
            row["Дата"] = day.Key.Date.ToString("dd.MM.yyyy");
            row["Сотрудник"] = day.Key.EmployeeName;
            foreach (var clinic in clinics)
            {
                var value = day.FirstOrDefault(x => string.Equals(x.Clinic, clinic, StringComparison.OrdinalIgnoreCase));
                row[$"{clinic}\nПЕРК"] = value?.Perk ?? 0;
                row[$"{clinic}\nПЛАН"] = value?.Plan ?? 0;
            }
            row["Итого\nПЕРК"] = day.Sum(x => x.Perk);
            row["Итого\nПЛАН"] = day.Sum(x => x.Plan);
            table.Rows.Add(row);
        }
        var total = table.NewRow();
        total["Дата"] = "ИТОГО";
        total["Сотрудник"] = "";
        foreach (DataColumn column in table.Columns.Cast<DataColumn>().Skip(2))
            total[column.ColumnName] = table.Rows.Cast<DataRow>().Sum(x => int.TryParse(x[column.ColumnName]?.ToString(), out var value) ? value : 0);
        table.Rows.Add(total);
        return table;
    }

    private void BuildClinicTable(IReadOnlyList<ClinicTableRow> source)
    {
        var clinics = CallCenterTopicCatalog.Clinics;
        ClinicTableGrid.Children.Clear(); ClinicTableGrid.ColumnDefinitions.Clear(); ClinicTableGrid.RowDefinitions.Clear();
        AddHeaderColumn(78); AddHeaderColumn(118);
        foreach (var _ in clinics) { AddHeaderColumn(62); AddHeaderColumn(62); }
        AddHeaderColumn(62); AddHeaderColumn(62);
        ClinicTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ClinicTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHeader("Дата", 0, 0, 1, 2); AddHeader("Сотрудник", 1, 0, 1, 2);
        var column = 2;
        foreach (var clinic in clinics)
        {
            AddHeader(clinic, column, 0, 2, 1);
            AddHeader("ПЕРК", column, 1, 1, 1); AddHeader("ПЛАН", column + 1, 1, 1, 1);
            column += 2;
        }
        AddHeader("Итого", column, 0, 2, 1); AddHeader("ПЕРК", column, 1, 1, 1); AddHeader("ПЛАН", column + 1, 1, 1, 1);
        var rowIndex = 2;
        var month = MonthComboBox.SelectedValue is int monthNumber && YearComboBox.SelectedItem is int year ? new DateTime(year, monthNumber, 1) : DateTime.Today;
        var employeeNames = source.Select(x => x.EmployeeName).Distinct().OrderBy(x => x).ToList();
        for (var date = month; date < month.AddMonths(1); date = date.AddDays(1))
        {
            foreach (var employee in employeeNames)
            {
                var day = source.Where(x => x.Date == date && x.EmployeeName == employee).ToList();
                ClinicTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddCell(date.ToString("dd.MM.yyyy"), 0, rowIndex, "#F8FAFC"); AddCell(employee, 1, rowIndex, "#F8FAFC");
                column = 2;
                for (var clinicIndex = 0; clinicIndex < clinics.Count; clinicIndex++)
                {
                    var x = day.FirstOrDefault(v => v.Clinic == clinics[clinicIndex]);
                    var shade = clinicIndex % 2 == 0 ? "#EAF1F8" : "#DCEAF7";
                    AddCell((x?.Perk ?? 0).ToString(), column++, rowIndex, shade); AddCell((x?.Plan ?? 0).ToString(), column++, rowIndex, shade);
                }
                AddCell(day.Sum(x => x.Perk).ToString(), column++, rowIndex, "#C7DCEF"); AddCell(day.Sum(x => x.Plan).ToString(), column, rowIndex, "#C7DCEF"); rowIndex++;
            }
        }
        ClinicTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell("ИТОГО", 0, rowIndex, "#B9D5EE"); AddCell("", 1, rowIndex, "#B9D5EE");
        column = 2;
        foreach (var clinic in clinics)
        {
            AddCell(source.Where(x => x.Clinic == clinic).Sum(x => x.Perk).ToString(), column++, rowIndex, "#B9D5EE");
            AddCell(source.Where(x => x.Clinic == clinic).Sum(x => x.Plan).ToString(), column++, rowIndex, "#B9D5EE");
        }
        AddCell(source.Sum(x => x.Perk).ToString(), column++, rowIndex, "#9FC5E8"); AddCell(source.Sum(x => x.Plan).ToString(), column, rowIndex, "#9FC5E8");
        rowIndex++;
        ClinicTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddSpanCell("ПЕРК + ПЛАН", 0, rowIndex, 2, "#D8E8F6");
        column = 2;
        foreach (var clinic in clinics)
        {
            var total = source.Where(x => x.Clinic == clinic).Sum(x => x.Perk + x.Plan);
            AddSpanCell(total.ToString(), column, rowIndex, 2, "#D8E8F6");
            column += 2;
        }
        AddSpanCell((source.Sum(x => x.Perk) + source.Sum(x => x.Plan)).ToString(), column, rowIndex, 2, "#B9D5EE");
    }

    private void AddHeaderColumn(double width) => ClinicTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
    private void AddHeader(string text, int column, int row, int columnSpan, int rowSpan)
    {
        var border = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), BorderThickness = new Thickness(0.5), Padding = new Thickness(2), Background = column % 4 < 2 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCEAF7")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAF1F8")) };
        border.Child = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(border, column); Grid.SetRow(border, row); Grid.SetColumnSpan(border, columnSpan); Grid.SetRowSpan(border, rowSpan);
        ClinicTableGrid.Children.Add(border);
    }

    private void AddCell(string text, int column, int row, string color)
    {
        var border = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), BorderThickness = new Thickness(0.5), Padding = new Thickness(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) };
        border.Child = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(border, column); Grid.SetRow(border, row); ClinicTableGrid.Children.Add(border);
    }

    private void AddSpanCell(string text, int column, int row, int span, string color)
    {
        AddCell(text, column, row, color);
        Grid.SetColumnSpan(ClinicTableGrid.Children[^1], span);
    }

    private void BuildDailyTable(IReadOnlyList<DailyTableRow> rows)
    {
        var columns = new[]
        {
            new TableColumn<DailyTableRow>("Дата", 92, x => x.Date.ToString("dd.MM.yyyy")),
            new TableColumn<DailyTableRow>("Сотрудник", 145, x => x.EmployeeName, true),
            new TableColumn<DailyTableRow>("Входящие", 82, x => x.Incoming.ToString()),
            new TableColumn<DailyTableRow>("Исходящие", 82, x => x.Outgoing.ToString()),
            new TableColumn<DailyTableRow>("ПЕРК", 66, x => x.Perk.ToString()),
            new TableColumn<DailyTableRow>("ПЛАН", 66, x => x.Plan.ToString()),
            new TableColumn<DailyTableRow>("Незапись", 82, x => x.NoAppointment.ToString()),
            new TableColumn<DailyTableRow>("Сбросы", 72, x => x.Drop.ToString()),
            new TableColumn<DailyTableRow>("Переводы", 78, x => x.Transfers.ToString()),
            new TableColumn<DailyTableRow>("% записи", 82, x => $"{x.AppointmentPercent:N1}%"),
            new TableColumn<DailyTableRow>("% незаписи", 94, x => $"{x.NoAppointmentPercent:N1}%")
        };
        BuildSimpleTable(DailyTableGrid, columns, rows);
    }

    private void BuildManualTable()
    {
        var columns = new[]
        {
            new TableColumn<ManualEntry>("Дата", 110, x => x.Date.ToString("dd.MM.yyyy")),
            new TableColumn<ManualEntry>("Тип", 180, x => x.Type),
            new TableColumn<ManualEntry>("Сотрудник", 190, x => x.EmployeeName, true),
            new TableColumn<ManualEntry>("Значение / комментарий", 280, x => x.Value)
        };
        BuildSimpleTable(ManualTableGrid, columns, _manualEntries.OrderByDescending(x => x.Date).ThenBy(x => x.EmployeeName));
    }

    private static void BuildSimpleTable<T>(Grid grid, IReadOnlyList<TableColumn<T>> columns, IEnumerable<T> source)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        foreach (var column in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width) });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            AddSimpleTableCell(grid, columns[columnIndex].Header, columnIndex, 0, GetColumnColor(columnIndex, true), true);

        var rowIndex = 1;
        foreach (var row in source)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                AddSimpleTableCell(grid, columns[columnIndex].Text(row), columnIndex, rowIndex, GetColumnColor(columnIndex, false), false, columns[columnIndex].AlignLeft);
            rowIndex++;
        }
    }

    private static string GetColumnColor(int columnIndex, bool isHeader)
    {
        if (columnIndex == 0) return isHeader ? "#D5E5F3" : "#F4F8FC";
        return columnIndex % 2 == 0
            ? (isHeader ? "#DCEAF7" : "#EAF1F8")
            : (isHeader ? "#C7DCEF" : "#DCEAF7");
    }

    private static void AddSimpleTableCell(Grid grid, string text, int column, int row, string color, bool isHeader, bool alignLeft = false)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4, 3, 4, 3),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        };
        border.Child = new TextBlock
        {
            Text = text,
            TextAlignment = alignLeft && !isHeader ? TextAlignment.Left : TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal
        };
        Grid.SetColumn(border, column);
        Grid.SetRow(border, row);
        grid.Children.Add(border);
    }

    private static string GetClinicName(string topic)
        => CallCenterTopicCatalog.TryGetClinic(topic, out var clinic) ? clinic : string.Empty;

    private static bool IsCallCenterEmployee(string name) => name.StartsWith("КЦ ", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Зоя Ершова", StringComparison.OrdinalIgnoreCase);

    private bool TryGetSelectedMonth(out DateTime from, out DateTime to)
    {
        from = default; to = default;
        if (MonthComboBox.SelectedValue is not int month || YearComboBox.SelectedItem is not int year) return false;
        from = new DateTime(year, month, 1);
        to = from.AddMonths(1).AddTicks(-1);
        return true;
    }

    private sealed class DailyTableRow
    {
        public DateTime Date { get; init; }
        public string EmployeeName { get; init; } = string.Empty;
        public int Incoming { get; init; }
        public int Outgoing { get; init; }
        public int Perk { get; init; }
        public int Plan { get; init; }
        public int NoAppointment { get; init; }
        public int Drop { get; init; }
        public int Transfers { get; init; }
        public double AppointmentPercent { get; init; }
        public double NoAppointmentPercent { get; init; }
    }

    private sealed class ClinicTableRow
    {
        public DateTime Date { get; init; }
        public string EmployeeName { get; init; } = string.Empty;
        public string Clinic { get; init; } = string.Empty;
        public int Perk { get; init; }
        public int Plan { get; init; }
    }

    private sealed class ManualEntry
    {
        public DateTime Date { get; init; }
        public string Type { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }

    private sealed record TableColumn<T>(string Header, double Width, Func<T, string> Text, bool AlignLeft = false);

    private sealed record MonthOption(int Number, string Name);
}
