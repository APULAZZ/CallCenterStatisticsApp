using System.Windows;
using System.Windows.Media;

namespace CallCenterStatisticsApp.UI;

public sealed class ThemeService
{
    public bool IsDark { get; private set; }
    public void Toggle() { IsDark = !IsDark; Apply(); }
    private void Apply()
    {
        var resources = Application.Current.Resources;
        resources["AppBackground"] = Brush(IsDark ? "#111827" : "#F6F8FC");
        resources["AppSurface"] = Brush(IsDark ? "#1F2937" : "#FFFFFF");
        resources["AppBorder"] = Brush(IsDark ? "#374151" : "#E2E8F0");
        resources["AppText"] = Brush(IsDark ? "#F8FAFC" : "#172033");
        resources["AppMutedText"] = Brush(IsDark ? "#CBD5E1" : "#64748B");
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
