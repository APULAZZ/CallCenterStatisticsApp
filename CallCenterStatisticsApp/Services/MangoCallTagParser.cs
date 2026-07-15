using System.Text.Json;

namespace CallCenterStatisticsApp.Services;

public static class MangoCallTagParser
{
    public static (string? Id, string? Name) GetFirstTag(JsonElement call)
    {
        foreach (var propertyName in new[] { "tag_id", "tags", "tag", "themes", "theme", "call_tags" })
        {
            if (!call.TryGetProperty(propertyName, out var value)) continue;
            var result = Read(value, propertyName is not "tag_id");
            if (result.Id is not null || result.Name is not null) return result;
        }
        var nested = FindNestedTag(call);
        if (nested.Id is not null || nested.Name is not null) return nested;
        return (null, null);
    }

    private static (string? Id, string? Name) FindNestedTag(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) { var result = FindNestedTag(item); if (result.Id is not null || result.Name is not null) return result; }
            return (null, null);
        }
        if (element.ValueKind != JsonValueKind.Object) return (null, null);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name.ToLowerInvariant();
            if (name.Contains("tag") || name.Contains("topic") || name.Contains("theme"))
            {
                var result = Read(property.Value, !name.EndsWith("_id", StringComparison.Ordinal));
                if (result.Id is not null || result.Name is not null) return result;
            }
            var nested = FindNestedTag(property.Value);
            if (nested.Id is not null || nested.Name is not null) return nested;
        }
        return (null, null);
    }

    private static (string? Id, string? Name) Read(JsonElement value, bool stringValuesAreNames)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) { var result = Read(item, stringValuesAreNames); if (result.Id is not null || result.Name is not null) return result; }
            return (null, null);
        }
        if (value.ValueKind == JsonValueKind.Number) return (value.ToString(), null);
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!stringValuesAreNames) return (text, null);
            var firstTag = text?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return (null, firstTag);
        }
        if (value.ValueKind != JsonValueKind.Object) return (null, null);
        string? id = FindString(value, "id") ?? FindString(value, "tag_id") ?? FindString(value, "tagId");
        string? name = FindString(value, "name") ?? FindString(value, "tag_name") ?? FindString(value, "tagName") ?? FindString(value, "title");
        if (id is not null || name is not null) return (id, name);
        foreach (var nested in value.EnumerateObject()) { var result = Read(nested.Value, stringValuesAreNames); if (result.Id is not null || result.Name is not null) return result; }
        return (null, null);
    }

    private static string? FindString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.ToString() : null;
    }
}
