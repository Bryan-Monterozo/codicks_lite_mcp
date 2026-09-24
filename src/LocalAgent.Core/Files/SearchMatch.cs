namespace LocalAgent.Core.Files;

public sealed record SearchMatch(
    string RelativePath,
    int LineNumber,
    string Excerpt);
