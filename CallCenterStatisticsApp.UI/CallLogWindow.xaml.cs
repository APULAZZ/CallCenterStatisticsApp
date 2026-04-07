using CallCenterStatisticsApp.Data;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace CallCenterStatisticsApp.UI;

public partial class CallLogWindow : Window
{
    private readonly AppDbContext _db;

    public CallLogWindow(AppDbContext db)
    {
        InitializeComponent();
        _db = db;

        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
        {
            MessageBox.Show("Укажите период.");
            return;
        }

        var from = FromDatePicker.SelectedDate.Value.Date;
        var to = ToDatePicker.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1);

        var query = _db.CallRecords
            .AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Group)
            .Include(x => x.Topic)
            .Where(x => x.CallDateTime >= from && x.CallDateTime <= to);

        var selectedDirection = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

        if (selectedDirection == "Входящие")
        {
            query = query.Where(x => x.IsIncoming);
        }
        else if (selectedDirection == "Исходящие")
        {
            query = query.Where(x => x.IsOutgoing);
        }

        var items = await query
            .OrderByDescending(x => x.CallDateTime)
            .Select(x => new CallLogRow
            {
                CallDateTime = x.CallDateTime,
                EmployeeName = x.Employee != null ? x.Employee.FullName : string.Empty,
                GroupName = x.Group != null ? x.Group.Name : string.Empty,
                TopicName = x.Topic != null ? x.Topic.Name : string.Empty,
                ExternalPhoneNumber = x.ExternalPhoneNumber,
                Direction = x.Direction,
                StatusText = x.StatusText,
                DurationSeconds = x.DurationSeconds,
                IsAnswered = x.IsAnswered,
                IsMissedIncoming = x.IsMissedIncoming,
                IsOutgoingNoAnswer = x.IsOutgoingNoAnswer
            })
            .ToListAsync();

        foreach (var item in items)
        {
            item.DirectionDisplay = item.Direction switch
            {
                "incoming" => "Входящий",
                "outgoing" => "Исходящий",
                "internal" => "Внутренний",
                _ => item.Direction ?? string.Empty
            };
        }

        CallsDataGrid.ItemsSource = items;
        SummaryTextBlock.Text = $"Записей: {items.Count}";
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

    private class CallLogRow
    {
        public DateTime CallDateTime { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string TopicName { get; set; } = string.Empty;
        public string? ExternalPhoneNumber { get; set; }
        public string? Direction { get; set; }
        public string DirectionDisplay { get; set; } = string.Empty;
        public string? StatusText { get; set; }
        public int? DurationSeconds { get; set; }
        public bool IsAnswered { get; set; }
        public bool IsMissedIncoming { get; set; }
        public bool IsOutgoingNoAnswer { get; set; }
    }
}