namespace LocalAgent.Core.Files;

public sealed record SearchPage(
    string Query,
    string RelativeRoot,
    IReadOnlyList<SearchMatch> Matches,
    bool IsPartial,
    string? StopReason);
