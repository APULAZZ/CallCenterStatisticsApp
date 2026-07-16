using System.Text.RegularExpressions;

namespace CallCenterStatisticsApp.Services;

/// <summary>
/// Единые правила для тематик MANGO. Исходное название хранится без изменений,
/// а этот справочник используется только для группировки и расчётов.
/// </summary>
public static class CallCenterTopicCatalog
{
    public static readonly IReadOnlyList<string> Clinics =
    [
        "Детство", "Сельма", "Баграмяна", "Регион", "ЦК",
        "Виктория", "Артиллерийская", "Генделя", "Альфа"
    ];

    public static string GetDisplayName(string? topicName)
    {
        var name = RemoveDirectionPrefix(topicName);
        var kind = GetKind(name);
        return TryGetClinic(name, out var clinic) && kind != CallTopicKind.Other
            ? $"{GetKindName(kind)} {clinic}"
            : name;
    }

    public static bool TryGetClinic(string? topicName, out string clinic)
    {
        var value = Normalize(RemoveDirectionPrefix(topicName));
        value = Regex.Replace(value, @"\b(ПЕРК|ПЛАН)\b", " ");
        value = Normalize(value);

        if (ContainsAny(value, "ДЕТСТВО", "МСК", "МОСКОВ")) { clinic = "Детство"; return true; }
        if (ContainsAny(value, "СЕЛЬМ")) { clinic = "Сельма"; return true; }
        if (ContainsAny(value, "МЕД", "БАГР")) { clinic = "Баграмяна"; return true; }
        if (ContainsAny(value, "РЕГИОН")) { clinic = "Регион"; return true; }
        if (ContainsAny(value, "ВИКТОРИ")) { clinic = "Виктория"; return true; }
        if (ContainsAny(value, "АРТИЛ", "АРТ")) { clinic = "Артиллерийская"; return true; }
        if (ContainsAny(value, "ГЕНДЕЛ")) { clinic = "Генделя"; return true; }
        if (ContainsAny(value, "АЛЬФ")) { clinic = "Альфа"; return true; }
        if (Regex.IsMatch(value, @"(^|\s)ЦК(\s|$)")) { clinic = "ЦК"; return true; }

        clinic = string.Empty;
        return false;
    }

    public static CallTopicKind GetKind(string? topicName)
    {
        var value = Normalize(topicName);
        if (value.Contains("ПЕРК", StringComparison.Ordinal)) return CallTopicKind.Perk;
        if (value.Contains("ПЛАН", StringComparison.Ordinal)) return CallTopicKind.Plan;
        if (value.Contains("НЕЗАПИС", StringComparison.Ordinal) || value.Contains("НЕ ЗАПИС", StringComparison.Ordinal)) return CallTopicKind.NoAppointment;
        if (value.Contains("СБРОС", StringComparison.Ordinal)) return CallTopicKind.Drop;
        return CallTopicKind.Other;
    }

    public static bool IsPerk(string? topicName) => GetKind(topicName) == CallTopicKind.Perk;
    public static bool IsPlan(string? topicName) => GetKind(topicName) == CallTopicKind.Plan;
    public static bool IsPerkOrPlan(string? topicName) => GetKind(topicName) is CallTopicKind.Perk or CallTopicKind.Plan;
    public static bool IsNoAppointment(string? topicName) => GetKind(topicName) == CallTopicKind.NoAppointment;
    public static bool IsDrop(string? topicName) => GetKind(topicName) == CallTopicKind.Drop;
    public static bool IsTransferTopic(string? topicName) => Normalize(topicName).Contains("ПЕРЕВОД", StringComparison.Ordinal);

    private static string GetKindName(CallTopicKind kind) => kind == CallTopicKind.Perk ? "ПЕРК" : "ПЛАН";
    private static bool ContainsAny(string value, params string[] parts) => parts.Any(value.Contains);
    private static string Normalize(string? value) => string.Join(" ", (value ?? string.Empty).Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string RemoveDirectionPrefix(string? topicName)
        => Regex.Replace(topicName?.Trim() ?? string.Empty, @"^(ВХОДЯЩИЙ|ИСХОДЯЩИЙ|ВХ\.?|ИСХ\.?)\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
}

public enum CallTopicKind
{
    Other,
    Perk,
    Plan,
    NoAppointment,
    Drop
}
