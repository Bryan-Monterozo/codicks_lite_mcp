namespace LocalAgent.Core.Files;

public interface IFileDiffService
{
    FileDiffResult CreateDiff(FileDiffRequest request);
}
