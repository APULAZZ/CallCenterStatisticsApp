using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CallCenterStatisticsApp.Services;

public class MangoApiClient : IMangoApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MangoApiOptions _options;

    public MangoApiClient(HttpClient httpClient, MangoApiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    #region Parsed methods

    public async Task<List<MangoUserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/config/users/request";
        var responseJson = await SendRequestAsync(endpoint, new { }, cancellationToken);
        return ParseUsers(responseJson);
    }

    public async Task<List<MangoGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/config/groups/request";
        var responseJson = await SendRequestAsync(endpoint, new { }, cancellationToken);
        return ParseGroups(responseJson);
    }

    public async Task<List<MangoTopicDto>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/cc/tags/";
        var responseJson = await SendRequestAsync(endpoint, new { }, cancellationToken);
        return ParseTopics(responseJson);
    }

    public async Task<List<MangoCallDto>> GetCallsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var responseJson = await GetCallsResponseJsonAsync(from, to, cancellationToken);
        return ParseCalls(responseJson);
    }

    private async Task<string> SendRequestWithPayloadDebugAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var sign = CalculateSign(json);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vpbx_api_key"] = _options.ApiKey,
            ["sign"] = sign,
            ["json"] = json
        });

        using var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl.TrimEnd('/')}{endpoint}",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP error calling '{endpoint}'. " +
                $"Status: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Payload: {json}. " +
                $"Response: {responseJson}");
        }

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("result", out var resultElement) &&
            resultElement.ValueKind == JsonValueKind.Number &&
            resultElement.TryGetInt32(out var resultCode) &&
            resultCode != 1000)
        {
            throw new InvalidOperationException(
                $"MANGO API returned error for endpoint '{endpoint}'. " +
                $"Result code: {resultCode}. " +
                $"Payload: {json}. " +
                $"Response: {responseJson}");
        }

        return responseJson;
    }

    #endregion

    #region Raw methods

    public async Task<string> GetUsersRawAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/config/users/request";
        return await SendRequestAsync(endpoint, new { }, cancellationToken);
    }

    public async Task<string> GetGroupsRawAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/config/groups/request";
        return await SendRequestAsync(endpoint, new { }, cancellationToken);
    }

    public async Task<string> GetTopicsRawAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/cc/tags/";
        return await SendRequestAsync(endpoint, new { }, cancellationToken);
    }

    public async Task<string> GetCallsRawAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await GetCallsResponseJsonAsync(from, to, cancellationToken);
    }

    #endregion

    #region Calls two-step workflow

    private async Task<string> GetCallsResponseJsonAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var key = await StartCallsStatisticsAsync(from, to, cancellationToken);
        var resultJson = await PollCallsStatisticsResultAsync(key, cancellationToken);
        return resultJson;
    }

    private async Task<string> StartCallsStatisticsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        const string endpoint = "/vpbx/stats/calls/request";

        var payload = new
        {
            start_date = from.ToString("dd.MM.yyyy HH:mm:ss"),
            end_date = to.ToString("dd.MM.yyyy HH:mm:ss"),
            limit = "1000",
            offset = "0"
        };

        var responseJson = await SendRequestWithPayloadDebugAsync(endpoint, payload, cancellationToken);

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("key", out var keyElement))
        {
            var key = keyElement.GetString();

            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        throw new InvalidOperationException(
            $"MANGO API did not return a key for calls statistics request. Payload: {JsonSerializer.Serialize(payload)}. Response: {responseJson}");
    }

    private async Task<string> PollCallsStatisticsResultAsync(string key, CancellationToken cancellationToken)
    {
        const string endpoint = "/vpbx/stats/calls/result/";
        const int maxAttempts = 30;
        const int initialDelayMs = 3000;
        const int pollDelayMs = 5000;

        await Task.Delay(initialDelayMs, cancellationToken);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(pollDelayMs, cancellationToken);

            var json = JsonSerializer.Serialize(new
            {
                key
            });

            var sign = CalculateSign(json);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["vpbx_api_key"] = _options.ApiKey,
                ["sign"] = sign,
                ["json"] = json
            });

            using var response = await _httpClient.PostAsync(
                $"{_options.BaseUrl.TrimEnd('/')}{endpoint}",
                content,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException("MANGO API returned 404 for calls result. Key not found or expired.");
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            if (root.TryGetProperty("status", out var statusElement))
            {
                var status = statusElement.GetString();

                if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase))
                    return responseJson;

                if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"MANGO API returned error status for calls result. Response: {responseJson}");

                if (string.Equals(status, "not-found", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"MANGO API returned not-found status for calls result. Response: {responseJson}");

                // request / work / cancel — продолжаем polling
                continue;
            }

            // Если status вдруг нет, но тело пришло — возвращаем как есть
            return responseJson;
        }

        throw new TimeoutException("Timeout while waiting for MANGO calls statistics result.");
    }

    #endregion

    #region Core request / sign

    private async Task<string> SendRequestAsync(string endpoint, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var sign = CalculateSign(json);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vpbx_api_key"] = _options.ApiKey,
            ["sign"] = sign,
            ["json"] = json
        });

        using var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl.TrimEnd('/')}{endpoint}",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureMangoApiSuccess(responseJson, endpoint);

        return responseJson;
    }

    private string CalculateSign(string json)
    {
        var input = _options.ApiKey + json + _options.ApiSalt;

        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void EnsureMangoApiSuccess(string responseJson, string endpoint)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("result", out var resultElement))
            return;

        if (resultElement.ValueKind != JsonValueKind.Number || !resultElement.TryGetInt32(out var resultCode))
            return;

        // Успех
        if (resultCode == 1000)
            return;

        // Если это ответ с key, а не ошибка, result может вообще отсутствовать — это уже обработано выше
        // Здесь кидаем исключение по всем явным кодам ошибок
        throw new InvalidOperationException(
            $"MANGO API returned error for endpoint '{endpoint}'. Result code: {resultCode}. Response: {responseJson}");
    }

    #endregion

    #region Parse users

    private static List<MangoUserDto> ParseUsers(string responseJson)
    {
        var result = new List<MangoUserDto>();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("users", out var usersElement) || usersElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in usersElement.EnumerateArray())
        {
            string? userId = null;
            string? name = null;
            string? extension = null;

            if (item.TryGetProperty("general", out var generalElement))
            {
                name = TryGetString(generalElement, "name");
                userId = TryGetString(generalElement, "user_id");
            }

            if (item.TryGetProperty("telephony", out var telephonyElement))
            {
                extension = TryGetString(telephonyElement, "extension");
            }

            result.Add(new MangoUserDto
            {
                Id = userId ?? extension,
                Name = name,
                Extension = extension
            });
        }

        return result;
    }

    #endregion

    #region Parse groups

    private static List<MangoGroupDto> ParseGroups(string responseJson)
    {
        var result = new List<MangoGroupDto>();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("groups", out var groupsElement) &&
            groupsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in groupsElement.EnumerateArray())
            {
                result.Add(new MangoGroupDto
                {
                    Id = TryGetString(item, "id"),
                    Name = TryGetString(item, "name")
                });
            }
        }

        return result;
    }

    #endregion

    #region Parse topics

    private static List<MangoTopicDto> ParseTopics(string responseJson)
    {
        var result = new List<MangoTopicDto>();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in tagsElement.EnumerateArray())
        {
            result.Add(new MangoTopicDto
            {
                Id = TryGetString(item, "id"),
                Name = TryGetString(item, "name")
            });
        }

        return result;
    }

    #endregion

    #region Parse calls

    private static List<MangoCallDto> ParseCalls(string responseJson)
    {
        var result = new List<MangoCallDto>();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var periodBlock in dataElement.EnumerateArray())
        {
            if (!periodBlock.TryGetProperty("list", out var listElement) || listElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in listElement.EnumerateArray())
            {
                var dto = ParseSingleCall(item);
                if (dto != null)
                    result.Add(dto);
            }
        }

        return result;
    }

    private static MangoCallDto? ParseSingleCall(JsonElement item)
    {
        var entryId = TryGetString(item, "entry_id");
        if (string.IsNullOrWhiteSpace(entryId))
            return null;

        var contextType = TryGetInt(item, "context_type");
        var contextStatus = TryGetInt(item, "context_status");

        string direction = contextType switch
        {
            1 => "incoming",
            2 => "outgoing",
            3 => "internal",
            _ => "unknown"
        };

        DateTime callDateTime = DateTime.MinValue;
        var startTimestamp = TryGetLong(item, "context_start_time");
        if (startTimestamp.HasValue && startTimestamp.Value > 0)
        {
            callDateTime = DateTimeOffset.FromUnixTimeSeconds(startTimestamp.Value).LocalDateTime;
        }

        var callerId = TryGetString(item, "caller_id");
        var callerName = TryGetString(item, "caller_name");

        string? employeeMangoId = null;
        string? employeeName = null;
        string? employeeExtension = null;

        string? groupMangoId = null;
        string? groupName = null;

        int? callEndReason = null;
        string? recordingId = null;

        if (item.TryGetProperty("context_calls", out var contextCalls) &&
            contextCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var cc in contextCalls.EnumerateArray())
            {
                var callType = TryGetString(cc, "call_type");

                // recording_id
                if (recordingId == null &&
                    cc.TryGetProperty("recording_id", out var recordingArray) &&
                    recordingArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rec in recordingArray.EnumerateArray())
                    {
                        recordingId = rec.ValueKind switch
                        {
                            JsonValueKind.String => rec.GetString(),
                            JsonValueKind.Number => rec.ToString(),
                            _ => null
                        };

                        if (!string.IsNullOrWhiteSpace(recordingId))
                            break;
                    }
                }

                if (direction == "outgoing" && callType == "number" && employeeExtension == null)
                {
                    employeeExtension = TryGetString(cc, "call_abonent_extension");
                    callEndReason ??= TryGetInt(cc, "call_end_reason");
                }

                if (callType == "group" && groupMangoId == null)
                {
                    groupMangoId = TryGetString(cc, "call_abonent_id");
                    groupName = TryGetString(cc, "call_abonent_info");
                    callEndReason ??= TryGetInt(cc, "call_end_reason");

                    if (cc.TryGetProperty("members", out var members) &&
                        members.ValueKind == JsonValueKind.Array)
                    {
                        JsonElement? answeredMember = null;
                        JsonElement? fallbackMember = null;

                        foreach (var member in members.EnumerateArray())
                        {
                            if (TryGetString(member, "call_type") != "user")
                                continue;

                            fallbackMember ??= member;

                            var memberAnswerTime = TryGetLong(member, "call_answer_time");
                            var memberTalkDuration = TryGetInt(member, "talk_duration");

                            if ((memberAnswerTime.HasValue && memberAnswerTime.Value > 0) ||
                                (memberTalkDuration.HasValue && memberTalkDuration.Value > 0))
                            {
                                answeredMember = member;
                                break;
                            }
                        }

                        var selectedMember = answeredMember ?? fallbackMember;

                        if (selectedMember.HasValue)
                        {
                            employeeMangoId ??= TryGetString(selectedMember.Value, "call_abonent_id");
                            employeeName ??= TryGetString(selectedMember.Value, "call_abonent_info");
                            employeeExtension ??= TryGetString(selectedMember.Value, "call_abonent_extension");
                            callEndReason ??= TryGetInt(selectedMember.Value, "call_end_reason");

                            if (recordingId == null &&
                                selectedMember.Value.TryGetProperty("recording_id", out var memberRecordingArray) &&
                                memberRecordingArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var rec in memberRecordingArray.EnumerateArray())
                                {
                                    recordingId = rec.ValueKind switch
                                    {
                                        JsonValueKind.String => rec.GetString(),
                                        JsonValueKind.Number => rec.ToString(),
                                        _ => null
                                    };

                                    if (!string.IsNullOrWhiteSpace(recordingId))
                                        break;
                                }
                            }
                        }
                    }
                }

                if (callType == "user" && employeeMangoId == null)
                {
                    employeeMangoId = TryGetString(cc, "call_abonent_id");
                    employeeName = TryGetString(cc, "call_abonent_info");
                    employeeExtension = TryGetString(cc, "call_abonent_extension");
                    callEndReason ??= TryGetInt(cc, "call_end_reason");
                }
            }
        }

        if (direction == "outgoing")
        {
            employeeMangoId ??= callerId;
            employeeName ??= callerName;
        }

        string? topicMangoId = null;
        if (item.TryGetProperty("tag_id", out var tagIdElement) &&
            tagIdElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagIdElement.EnumerateArray())
            {
                topicMangoId = tag.ValueKind switch
                {
                    JsonValueKind.Number => tag.ToString(),
                    JsonValueKind.String => tag.GetString(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(topicMangoId))
                    break;
            }
        }

        var callerNumber = TryGetString(item, "caller_number");
        var calledNumber = TryGetString(item, "called_number");

        string? phoneNumber = direction switch
        {
            "incoming" => callerNumber,
            "outgoing" => calledNumber,
            _ => callerNumber ?? calledNumber
        };

        string? statusCode = contextStatus?.ToString();
        string? statusText = contextStatus switch
        {
            0 => "unsuccessful",
            1 => "successful",
            _ => null
        };

        return new MangoCallDto
        {
            CallId = entryId,
            CallDateTime = callDateTime,
            Direction = direction,

            EmployeeMangoId = employeeMangoId,
            EmployeeName = employeeName,
            EmployeeExtension = employeeExtension,

            GroupMangoId = groupMangoId,
            GroupName = groupName,

            TopicMangoId = topicMangoId,
            RecordingId = recordingId,

            PhoneNumber = phoneNumber,

            StatusCode = statusCode,
            StatusText = statusText,

            DurationSeconds = TryGetInt(item, "duration"),
            TalkDurationSeconds = TryGetInt(item, "talk_duration"),
            WaitDurationSeconds = null,

            CallEndReason = callEndReason,

            RawJson = item.GetRawText()
        };
    }

    #endregion

    #region Helpers

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => property.ToString()
        };
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            return value;

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            return value;

        return null;
    }

    public async Task<List<MangoRecordingCategoryDto>> GetRecordingCategoriesAsync(
    string recordingId,
    CancellationToken cancellationToken = default)
    {
        const string endpoint = "/vpbx/queries/recording_categories";

        var payload = new
        {
            recording_id = $"[\"{recordingId}\"]",
            with_terms = true,
            with_names = true
        };

        var responseJson = await SendRequestAsync(endpoint, payload, cancellationToken);

        return ParseRecordingCategories(responseJson);
    }

    private static List<MangoRecordingCategoryDto> ParseRecordingCategories(string responseJson)
    {
        var result = new List<MangoRecordingCategoryDto>();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in dataElement.EnumerateArray())
        {
            var recordingId = TryGetString(item, "recording_id");

            if (!item.TryGetProperty("categories", out var categoriesElement) ||
                categoriesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var category in categoriesElement.EnumerateArray())
            {
                result.Add(new MangoRecordingCategoryDto
                {
                    RecordingId = recordingId,
                    CategoryId = TryGetInt(category, "id"),
                    CategoryName = TryGetString(category, "name")
                });
            }
        }

        return result;
    }

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
            return value;

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value))
            return value;

        return null;
    }

    #endregion
}