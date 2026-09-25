using System.Text.Json;
using System.Text.Json.Serialization;
using LocalAgent.Core.Security;

namespace LocalAgent.Infrastructure.Security;

public sealed class LocalSessionControlProcessor(
    ISessionGuard sessionGuard)
{
    public string Process(
        string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(
                requestJson))
        {
            return Serialize(
                new LocalSessionControlResponse(
                    false,
                    "Invalid request.",
                    null));
        }

        LocalSessionControlRequest? request;

        try
        {
            request =
                JsonSerializer.Deserialize<LocalSessionControlRequest>(
                    requestJson,
                    JsonOptions);
        }
        catch (JsonException)
        {
            request =
                null;
        }

        if (request is null)
        {
            return Serialize(
                new LocalSessionControlResponse(
                    false,
                    "Invalid request.",
                    null));
        }

        return Serialize(
            ProcessRequest(
                request));
    }

    public static string SerializeRequest(
        LocalSessionControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        return JsonSerializer.Serialize(
            request,
            JsonOptions);
    }

    public static LocalSessionControlResponse DeserializeResponse(
        string responseJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            responseJson);

        return JsonSerializer.Deserialize<LocalSessionControlResponse>(
                   responseJson,
                   JsonOptions)
               ?? throw new InvalidDataException(
                   "Local session-control response was invalid.");
    }

    public static string CreateTransportError(
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            message);

        return Serialize(
            new LocalSessionControlResponse(
                false,
                message,
                null));
    }

    private LocalSessionControlResponse ProcessRequest(
        LocalSessionControlRequest request)
    {
        if (string.Equals(
                request.Action,
                "status",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LocalSessionControlResponse(
                true,
                null,
                sessionGuard.GetStatus());
        }

        if (string.Equals(
                request.Action,
                "lock",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LocalSessionControlResponse(
                true,
                null,
                sessionGuard.Lock());
        }

        if (!string.Equals(
                request.Action,
                "unlock",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LocalSessionControlResponse(
                false,
                "Unknown action.",
                null);
        }

        if (!Enum.TryParse<AgentAccessMode>(
                request.Mode,
                ignoreCase: true,
                out var mode))
        {
            return new LocalSessionControlResponse(
                false,
                "Mode must be ReadOnly or Full.",
                null);
        }

        var success =
            sessionGuard.TryUnlock(
                mode,
                request.Otp ??
                    string.Empty,
                request.LeaseMinutes,
                out var errorMessage);

        return new LocalSessionControlResponse(
            success,
            errorMessage,
            sessionGuard.GetStatus());
    }

    private static string Serialize(
        LocalSessionControlResponse response) =>
        JsonSerializer.Serialize(
            response,
            JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}
