using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CallCenterStatisticsApp.UI;

public partial class CallDetailsWindow : Window
{
    private readonly AppDbContext _db;
    private readonly IMangoApiClient _api;
    private string? _recordingId;
    private string? _temporaryAudioPath;
    private readonly Slider _positionSlider = new() { Minimum = 0, IsEnabled = false, Margin = new Thickness(0, 14, 0, 0) };
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _isPlaying;

    public CallDetailsWindow(AppDbContext db, IMangoApiClient api)
    {
        InitializeComponent(); _db = db; _api = api;
        ((StackPanel)AudioPlayer.Parent).Children.Insert(3, _positionSlider);
        AudioPlayer.MediaOpened += (_, _) => { if (AudioPlayer.NaturalDuration.HasTimeSpan) { _positionSlider.Maximum = AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds; _positionSlider.IsEnabled = true; } };
        AudioPlayer.MediaEnded += (_, _) => { _positionTimer.Stop(); _isPlaying = false; PlayButton.Content = "▶ Прослушать"; RecordingTextBlock.Text = "Воспроизведение завершено."; };
        AudioPlayer.MediaFailed += (_, args) => { _positionTimer.Stop(); _isPlaying = false; PlayButton.Content = "▶ Прослушать"; RecordingTextBlock.Text = $"Запись не удалось открыть: {args.ErrorException.Message}"; };
        _positionSlider.AddHandler(Slider.PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler((_, _) => AudioPlayer.Position = TimeSpan.FromSeconds(_positionSlider.Value)));
        _positionTimer.Tick += (_, _) => { if (!AudioPlayer.NaturalDuration.HasTimeSpan) return; _positionSlider.Value = Math.Min(AudioPlayer.Position.TotalSeconds, _positionSlider.Maximum); };
    }

    public async Task LoadAsync(int callId)
    {
        var call = await _db.CallRecords.AsNoTracking().Include(x => x.Employee).Include(x => x.Group).Include(x => x.Topic).SingleOrDefaultAsync(x => x.Id == callId);
        if (call is null) { Close(); return; }
        DateTextBlock.Text = call.CallDateTime.ToString("dd.MM.yyyy HH:mm");
        EmployeeTextBlock.Text = call.Employee?.FullName ?? "—"; GroupTextBlock.Text = call.Group?.Name ?? "—"; PhoneTextBlock.Text = call.ExternalPhoneNumber ?? "—";
        DirectionTextBlock.Text = call.IsIncoming ? "Входящий" : call.IsOutgoing ? "Исходящий" : "Внутренний"; TopicTextBlock.Text = call.Topic?.Name ?? "—";
        DurationTextBlock.Text = call.DurationSeconds is > 0 ? TimeSpan.FromSeconds(call.DurationSeconds.Value).ToString(@"m\:ss") : "—";
        _recordingId = call.RecordingId;
        var hasRecording = !string.IsNullOrWhiteSpace(_recordingId);
        RecordingTextBlock.Text = hasRecording ? "Запись доступна в Mango Office." : "Для этого звонка запись не найдена.";
        PlayButton.IsEnabled = DownloadButton.IsEnabled = hasRecording;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_recordingId)) return;
        if (_isPlaying)
        {
            AudioPlayer.Pause(); _positionTimer.Stop(); _isPlaying = false; PlayButton.Content = "▶ Прослушать"; RecordingTextBlock.Text = "Воспроизведение приостановлено.";
            return;
        }
        try
        {
            PlayButton.IsEnabled = false; RecordingTextBlock.Text = "Загружаем запись…";
            if (string.IsNullOrWhiteSpace(_temporaryAudioPath) || !File.Exists(_temporaryAudioPath))
            {
                var file = await _api.GetRecordingAsync(_recordingId, forDownload: false);
                _temporaryAudioPath = Path.Combine(Path.GetTempPath(), $"mango-{Guid.NewGuid():N}.mp3");
                await File.WriteAllBytesAsync(_temporaryAudioPath, file.Content);
                AudioPlayer.Source = new Uri(_temporaryAudioPath, UriKind.Absolute);
            }
            AudioPlayer.Play(); _positionTimer.Start(); _isPlaying = true; PlayButton.Content = "❚❚ Пауза";
            RecordingTextBlock.Text = "Воспроизводится запись.";
        }
        catch (Exception ex) { RecordingTextBlock.Text = $"Не удалось воспроизвести запись: {ex.Message}"; }
        finally { PlayButton.IsEnabled = true; }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_recordingId)) return;
        var dialog = new SaveFileDialog { Filter = "Аудиозапись MP3|*.mp3", FileName = $"Звонок_{DateTime.Now:yyyyMMdd_HHmm}.mp3" };
        if (dialog.ShowDialog(this) != true) return;
        try { DownloadButton.IsEnabled = false; RecordingTextBlock.Text = "Скачиваем запись…"; var file = await _api.GetRecordingAsync(_recordingId, forDownload: true); await File.WriteAllBytesAsync(dialog.FileName, file.Content); RecordingTextBlock.Text = "Запись сохранена."; }
        catch (Exception ex) { RecordingTextBlock.Text = $"Не удалось скачать запись: {ex.Message}"; }
        finally { DownloadButton.IsEnabled = true; }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosed(EventArgs e) { _positionTimer.Stop(); AudioPlayer.Stop(); AudioPlayer.Close(); base.OnClosed(e); }
}
