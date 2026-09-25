namespace LocalAgent.Core.Security;

public sealed record LocalSessionControlRequest(
    string Action,
    string? Mode = null,
    string? Otp = null,
    int? LeaseMinutes = null);

public sealed record LocalSessionControlResponse(
    bool Success,
    string? Error,
    SessionStatus? Status);

public interface ILocalSessionControlClient
{
    Task<LocalSessionControlResponse> SendAsync(
        LocalSessionControlRequest request,
        CancellationToken cancellationToken = default);
}
