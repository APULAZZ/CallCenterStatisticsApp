using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Models;
using CallCenterStatisticsApp.Services;
using CallCenterStatisticsApp.UI;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace CallCenterStatisticsApp.UI.Views;

/// <summary>Локальная замена сводных листов Google Sheets за 2026 год.</summary>
public partial class AnnualSummaryPage : UserControl
{
    private const string StoreKey = "AnnualCallCenterSummaryTables";
    private readonly AppDbContext _db;
    private readonly MangoCallImportService _callImport;
    private readonly BusyService _busy;
    private AnnualSummaryManualStore _store = new();
    private bool _loaded;

    public AnnualSummaryPage(AppDbContext db, MangoCallImportService callImport, BusyService busy)
    {
        InitializeComponent();
        _db = db;
        _callImport = callImport;
        _busy = busy;
        SummaryYearComboBox.ItemsSource = Enumerable.Range(DateTime.Today.Year - 2, 5).ToList();
        SummaryYearComboBox.SelectedItem = DateTime.Today.Year;
        SummaryMonthComboBox.ItemsSource = Enumerable.Range(1, 12).Select(x => new MonthOption(x, new DateTime(2000, x, 1).ToString("MMMM"))).ToList();
        SummaryMonthComboBox.DisplayMemberPath = nameof(MonthOption.Name);
        SummaryMonthComboBox.SelectedValuePath = nameof(MonthOption.Number);
        SummaryMonthComboBox.SelectedValue = DateTime.Today.Month;
        ConfigureGrids();
    }

    public async Task LoadAsync()
    {
        if (!_loaded)
        {
            var setting = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == StoreKey);
            _store = setting is null ? new AnnualSummaryManualStore() : JsonSerializer.Deserialize<AnnualSummaryManualStore>(setting.Value) ?? new AnnualSummaryManualStore();
            _loaded = true;
        }
        await LoadDataAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var year = SelectedYear;
        var from = new DateTime(year, SelectedMonth, 1);
        if (from.Date > DateTime.Today)
        {
            await LoadDataAsync();
            return;
        }
        var to = new[] { from.AddMonths(1).AddTicks(-1), DateTime.Today.AddDays(1).AddTicks(-1) }.Min();
        using var operation = _busy.Begin("Синхронизируем выбранный месяц Mango и обновляем сводные таблицы…");
        await _callImport.EnsurePeriodImportedAsync(from, to);
        await LoadDataAsync();
    }

    private async void PeriodSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) await LoadDataAsync();
    }

    private void ManualGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            await SaveStoreAsync();
            await LoadDataAsync();
        });
    }

    private int SelectedYear => SummaryYearComboBox.SelectedItem is int year ? year : DateTime.Today.Year;
    private int SelectedMonth => SummaryMonthComboBox.SelectedValue is int month ? month : DateTime.Today.Month;

    private async Task LoadDataAsync()
    {
        var year = SelectedYear;
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);
        var topics = await _db.CallTopics.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        var employeeNames = (await _db.Employees.AsNoTracking().Select(x => x.FullName).ToListAsync())
            .Where(IsCallCenterEmployee).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var calls = await _db.CallRecords.AsNoTracking().Include(x => x.Employee)
            .Where(x => x.CallDateTime >= from && x.CallDateTime < to && x.EmployeeId.HasValue)
            .ToListAsync();
        var relevantCalls = calls.Where(x => x.Employee is not null && IsCallCenterEmployee(x.Employee.FullName)).ToList();
        var employeeRowsByMonth = new Dictionary<int, List<EmployeeMetricRow>>();
        var summaryRows = new List<AnnualSummaryRow>();

        for (var month = 1; month <= 12; month++)
        {
            var monthCalls = relevantCalls.Where(x => x.CallDateTime.Month == month).ToList();
            var employeeRows = employeeNames.Select(name => CreateEmployeeRow(year, month, name,
                monthCalls.Where(x => string.Equals(x.Employee!.FullName, name, StringComparison.OrdinalIgnoreCase)).ToList(), topics)).ToList();
            employeeRowsByMonth[month] = employeeRows;
            var manual = GetSummaryManual(year, month);
            var incoming = monthCalls.Count(x => x.IsIncoming && x.IsAnswered);
            var perk = monthCalls.Count(x => CallCenterTopicCatalog.IsPerk(TopicName(x, topics)));
            var plan = monthCalls.Count(x => CallCenterTopicCatalog.IsPlan(TopicName(x, topics)));
            var missed = monthCalls.Count(x => x.IsMissedIncoming);
            var transfers = monthCalls.Count(x => CallCenterTopicCatalog.IsTransferTopic(TopicName(x, topics)));
            var drops = monthCalls.Count(x => CallCenterTopicCatalog.IsDrop(TopicName(x, topics)));
            var booked = employeeRows.Sum(x => x.TotalBooked);
            var denominator = incoming - employeeRows.Sum(x => x.InformationCalls) - transfers - drops;
            summaryRows.Add(new AnnualSummaryRow(month, incoming, perk, plan, missed, transfers, drops, manual,
                denominator <= 0 ? 0 : Math.Round(100d * booked / denominator, 2)));
        }

        AnnualSummaryGrid.ItemsSource = summaryRows;
        EmployeeMetricsGrid.ItemsSource = employeeRowsByMonth[SelectedMonth];
        KefGrid.ItemsSource = employeeNames.Select(name => CreateKefRow(year, name, employeeRowsByMonth)).ToList();
        AttendanceGrid.ItemsSource = employeeNames.Select(name => GetScoreRow(_store.Attendance, year, name)).ToList();
        PhoneScoreGrid.ItemsSource = employeeNames.Select(name => GetScoreRow(_store.PhoneScores, year, name)).ToList();
    }

    private EmployeeMetricRow CreateEmployeeRow(int year, int month, string employeeName, IReadOnlyCollection<CallRecord> calls, IReadOnlyDictionary<int, string> topics)
    {
        var manual = GetEmployeeManual(year, month, employeeName);
        var incoming = calls.Count(x => x.IsIncoming && x.IsAnswered);
        var outgoingPerk = calls.Count(x => x.IsOutgoing && CallCenterTopicCatalog.IsPerk(TopicName(x, topics)));
        var outgoingPlan = calls.Count(x => x.IsOutgoing && CallCenterTopicCatalog.IsPlan(TopicName(x, topics)));
        var transfers = calls.Count(x => CallCenterTopicCatalog.IsTransferTopic(TopicName(x, topics)));
        var drops = calls.Count(x => CallCenterTopicCatalog.IsDrop(TopicName(x, topics)));
        return new EmployeeMetricRow(employeeName, incoming, outgoingPerk, outgoingPlan, transfers, drops, manual);
    }

    private KefRow CreateKefRow(int year, string employeeName, IReadOnlyDictionary<int, List<EmployeeMetricRow>> rowsByMonth)
    {
        var profile = GetProfile(employeeName);
        var values = Enumerable.Range(1, 12).Select(month => rowsByMonth[month].First(x => x.EmployeeName == employeeName).Kef).ToArray();
        return new KefRow(employeeName, profile, values);
    }

    private AnnualSummaryManual GetSummaryManual(int year, int month)
    {
        var value = _store.Summaries.FirstOrDefault(x => x.Year == year && x.Month == month);
        if (value is not null) return value;
        value = new AnnualSummaryManual { Year = year, Month = month };
        _store.Summaries.Add(value);
        return value;
    }

    private EmployeeMonthManual GetEmployeeManual(int year, int month, string employeeName)
    {
        var value = _store.EmployeeMetrics.FirstOrDefault(x => x.Year == year && x.Month == month && string.Equals(x.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase));
        if (value is not null) return value;
        value = new EmployeeMonthManual { Year = year, Month = month, EmployeeName = employeeName };
        _store.EmployeeMetrics.Add(value);
        return value;
    }

    private EmployeeProfile GetProfile(string employeeName)
    {
        var value = _store.Profiles.FirstOrDefault(x => string.Equals(x.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase));
        if (value is not null) return value;
        value = new EmployeeProfile { EmployeeName = employeeName };
        _store.Profiles.Add(value);
        return value;
    }

    private AnnualManualScore GetScoreRow(List<AnnualManualScore> source, int year, string employeeName)
    {
        var value = source.FirstOrDefault(x => x.Year == year && string.Equals(x.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase));
        if (value is not null) return value;
        value = new AnnualManualScore { Year = year, EmployeeName = employeeName };
        source.Add(value);
        return value;
    }

    private async Task SaveStoreAsync()
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == StoreKey);
        if (setting is null)
        {
            setting = new AppSetting { Key = StoreKey };
            _db.AppSettings.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(_store);
        await _db.SaveChangesAsync();
    }

    private void ConfigureGrids()
    {
        ConfigureGrid(AnnualSummaryGrid, new[]
        {
            Column("Месяц", nameof(AnnualSummaryRow.MonthName), 110, false, true), Column("Принятые входящие", nameof(AnnualSummaryRow.Incoming), 145),
            Column("ПЕРК + ПЛАН", nameof(AnnualSummaryRow.TotalTopics), 110), Column("ПЕРК", nameof(AnnualSummaryRow.Perk), 75), Column("ПЛАН", nameof(AnnualSummaryRow.Plan), 75),
            Column("Пропущенные", nameof(AnnualSummaryRow.Missed), 110), Column("ПЕРК явка", nameof(AnnualSummaryRow.PerkAttendance), 100, true), Column("ПЕРК неявка", nameof(AnnualSummaryRow.PerkNoShow), 105, true),
            Column("Информационные + объект", nameof(AnnualSummaryRow.InformationCalls), 185, true), Column("Перевод на филиал", nameof(AnnualSummaryRow.Transfers), 150), Column("Сброс", nameof(AnnualSummaryRow.Drops), 75),
            Column("КЭФ, %", nameof(AnnualSummaryRow.Kef), 80), Column("Пояснения", nameof(AnnualSummaryRow.Explanation), 280, true, true)
        });
        ConfigureGrid(EmployeeMetricsGrid, new[]
        {
            Column("Сотрудник", nameof(EmployeeMetricRow.EmployeeName), 165, false, true), Column("Принятые", nameof(EmployeeMetricRow.Incoming), 90), Column("Всего записано", nameof(EmployeeMetricRow.TotalBooked), 115),
            Column("ПЕРК / обращения", nameof(EmployeeMetricRow.PerkAppeal), 125, true), Column("ПЛАН / обращения", nameof(EmployeeMetricRow.PlanAppeal), 125, true), Column("Исх. ПЕРК", nameof(EmployeeMetricRow.OutgoingPerk), 95),
            Column("Исх. ПЛАН", nameof(EmployeeMetricRow.OutgoingPlan), 95), Column("ПЕРК явка", nameof(EmployeeMetricRow.PerkAttendance), 100, true), Column("Неявка", nameof(EmployeeMetricRow.NoShow), 80, true),
            Column("Информационные", nameof(EmployeeMetricRow.InformationCalls), 115, true), Column("Перевод", nameof(EmployeeMetricRow.Transfers), 80), Column("Сброс", nameof(EmployeeMetricRow.Drops), 75), Column("КЭФ, %", nameof(EmployeeMetricRow.Kef), 80)
        });
        ConfigureGrid(KefGrid, new[] { Column("Сотрудник", nameof(KefRow.EmployeeName), 160, false, true), Column("Стаж", nameof(KefRow.Experience), 100, true, true) }
            .Concat(MonthColumns<KefRow>(x => x.GetMonthValue)).Append(Column("Средний КЭФ", nameof(KefRow.Average), 105)).ToArray());
        ConfigureGrid(AttendanceGrid, new[] { Column("Сотрудник", nameof(AnnualManualScore.EmployeeName), 160, false, true) }
            .Concat(MonthColumns<AnnualManualScore>(x => x.GetMonthValue, true)).Append(Column("Итого", nameof(AnnualManualScore.Average), 82)).ToArray());
        ConfigureGrid(PhoneScoreGrid, new[] { Column("Сотрудник", nameof(AnnualManualScore.EmployeeName), 160, false, true) }
            .Concat(MonthColumns<AnnualManualScore>(x => x.GetMonthValue, true)).Append(Column("Итого", nameof(AnnualManualScore.Average), 82)).ToArray());
    }

    private static IEnumerable<ColumnDefinition> MonthColumns<T>(Func<T, Func<int, double>> getter, bool editable = false)
        => Enumerable.Range(1, 12).Select(month => Column(new DateTime(2000, month, 1).ToString("MMMM"), $"M{month:00}", 86, editable));

    private static ColumnDefinition Column(string header, string property, double width, bool editable = false, bool alignLeft = false)
        => new(header, property, width, editable, alignLeft);

    private static void ConfigureGrid(DataGrid grid, IReadOnlyList<ColumnDefinition> columns)
    {
        grid.Columns.Clear();
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        grid.HorizontalGridLinesBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
        grid.VerticalGridLinesBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
        grid.CanUserSortColumns = false;
        grid.RowHeaderWidth = 0;
        grid.Background = Brushes.White;
        for (var index = 0; index < columns.Count; index++)
        {
            var item = columns[index];
            var column = new DataGridTextColumn
            {
                Header = item.Header,
                Binding = new Binding(item.Property) { Mode = item.Editable ? BindingMode.TwoWay : BindingMode.OneWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
                Width = item.Width,
                IsReadOnly = !item.Editable,
                CellStyle = CreateCellStyle(index, item.AlignLeft),
                HeaderStyle = CreateHeaderStyle(index)
            };
            grid.Columns.Add(column);
        }
    }

    private static Style CreateHeaderStyle(int index)
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFor(index, true)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0.5)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 5, 4, 5)));
        return style;
    }

    private static Style CreateCellStyle(int index, bool alignLeft)
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFor(index, false)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0.5)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 3, 4, 3)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center));
        return style;
    }

    private static Brush BrushFor(int index, bool header)
    {
        var color = index == 0 ? (header ? "#D5E5F3" : "#F4F8FC")
            : index % 2 == 0 ? (header ? "#DCEAF7" : "#EAF1F8")
            : (header ? "#C7DCEF" : "#DCEAF7");
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private static string TopicName(CallRecord call, IReadOnlyDictionary<int, string> topics)
        => call.TopicId.HasValue && topics.TryGetValue(call.TopicId.Value, out var value) ? value : string.Empty;
    private static bool IsCallCenterEmployee(string name) => name.StartsWith("КЦ ", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Зоя Ершова", StringComparison.OrdinalIgnoreCase);

    private sealed record ColumnDefinition(string Header, string Property, double Width, bool Editable, bool AlignLeft);
    private sealed record MonthOption(int Number, string Name);
}

public sealed class AnnualSummaryRow
{
    private readonly AnnualSummaryManual _manual;
    public AnnualSummaryRow(int month, int incoming, int perk, int plan, int missed, int transfers, int drops, AnnualSummaryManual manual, double kef)
        => (Month, Incoming, Perk, Plan, Missed, Transfers, Drops, _manual, Kef) = (month, incoming, perk, plan, missed, transfers, drops, manual, kef);
    public int Month { get; }
    public string MonthName => new DateTime(2000, Month, 1).ToString("MMMM");
    public int Incoming { get; }
    public int Perk { get; }
    public int Plan { get; }
    public int TotalTopics => Perk + Plan;
    public int Missed { get; }
    public int Transfers { get; }
    public int Drops { get; }
    public int PerkAttendance { get => _manual.PerkAttendance; set => _manual.PerkAttendance = value; }
    public int PerkNoShow { get => _manual.PerkNoShow; set => _manual.PerkNoShow = value; }
    public int InformationCalls { get => _manual.InformationCalls; set => _manual.InformationCalls = value; }
    public string Explanation { get => _manual.Explanation; set => _manual.Explanation = value ?? string.Empty; }
    public double Kef { get; }
}

public sealed class EmployeeMetricRow
{
    private readonly EmployeeMonthManual _manual;
    public EmployeeMetricRow(string employeeName, int incoming, int outgoingPerk, int outgoingPlan, int transfers, int drops, EmployeeMonthManual manual)
        => (EmployeeName, Incoming, OutgoingPerk, OutgoingPlan, Transfers, Drops, _manual) = (employeeName, incoming, outgoingPerk, outgoingPlan, transfers, drops, manual);
    public string EmployeeName { get; }
    public int Incoming { get; }
    public int OutgoingPerk { get; }
    public int OutgoingPlan { get; }
    public int Transfers { get; }
    public int Drops { get; }
    public int PerkAppeal { get => _manual.PerkAppeal; set => _manual.PerkAppeal = value; }
    public int PlanAppeal { get => _manual.PlanAppeal; set => _manual.PlanAppeal = value; }
    public int TotalBooked => PerkAppeal + PlanAppeal;
    public int PerkAttendance { get => _manual.PerkAttendance; set => _manual.PerkAttendance = value; }
    public int NoShow { get => _manual.NoShow; set => _manual.NoShow = value; }
    public int InformationCalls { get => _manual.InformationCalls; set => _manual.InformationCalls = value; }
    public double Kef => Incoming - InformationCalls - Transfers - Drops <= 0 ? 0 : Math.Round(100d * TotalBooked / (Incoming - InformationCalls - Transfers - Drops), 2);
}

public sealed class KefRow
{
    private readonly EmployeeProfile _profile;
    private readonly double[] _values;
    public KefRow(string employeeName, EmployeeProfile profile, double[] values) => (EmployeeName, _profile, _values) = (employeeName, profile, values);
    public string EmployeeName { get; }
    public string Experience { get => _profile.Experience; set => _profile.Experience = value ?? string.Empty; }
    public double M01 => _values[0]; public double M02 => _values[1]; public double M03 => _values[2]; public double M04 => _values[3]; public double M05 => _values[4]; public double M06 => _values[5];
    public double M07 => _values[6]; public double M08 => _values[7]; public double M09 => _values[8]; public double M10 => _values[9]; public double M11 => _values[10]; public double M12 => _values[11];
    public double Average => Math.Round(_values.Where(x => x > 0).DefaultIfEmpty().Average(), 2);
    public Func<int, double> GetMonthValue => month => _values[month - 1];
}

public sealed class AnnualSummaryManualStore
{
    public List<AnnualSummaryManual> Summaries { get; set; } = [];
    public List<EmployeeMonthManual> EmployeeMetrics { get; set; } = [];
    public List<EmployeeProfile> Profiles { get; set; } = [];
    public List<AnnualManualScore> Attendance { get; set; } = [];
    public List<AnnualManualScore> PhoneScores { get; set; } = [];
}

public sealed class AnnualSummaryManual { public int Year { get; set; } public int Month { get; set; } public int PerkAttendance { get; set; } public int PerkNoShow { get; set; } public int InformationCalls { get; set; } public string Explanation { get; set; } = string.Empty; }
public sealed class EmployeeMonthManual { public int Year { get; set; } public int Month { get; set; } public string EmployeeName { get; set; } = string.Empty; public int PerkAppeal { get; set; } public int PlanAppeal { get; set; } public int PerkAttendance { get; set; } public int NoShow { get; set; } public int InformationCalls { get; set; } }
public sealed class EmployeeProfile { public string EmployeeName { get; set; } = string.Empty; public string Experience { get; set; } = string.Empty; }
public sealed class AnnualManualScore
{
    public int Year { get; set; } public string EmployeeName { get; set; } = string.Empty;
    public double M01 { get; set; } public double M02 { get; set; } public double M03 { get; set; } public double M04 { get; set; } public double M05 { get; set; } public double M06 { get; set; }
    public double M07 { get; set; } public double M08 { get; set; } public double M09 { get; set; } public double M10 { get; set; } public double M11 { get; set; } public double M12 { get; set; }
    public double Average => Math.Round(new[] { M01, M02, M03, M04, M05, M06, M07, M08, M09, M10, M11, M12 }.Where(x => x > 0).DefaultIfEmpty().Average(), 2);
    public Func<int, double> GetMonthValue => month => month switch { 1 => M01, 2 => M02, 3 => M03, 4 => M04, 5 => M05, 6 => M06, 7 => M07, 8 => M08, 9 => M09, 10 => M10, 11 => M11, _ => M12 };
}
