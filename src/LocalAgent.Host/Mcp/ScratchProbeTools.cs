using System.ComponentModel;
using LocalAgent.Core.Security;
using LocalAgent.Infrastructure.Scratch;
using ModelContextProtocol.Server;

namespace LocalAgent.Host.Mcp;

[McpServerToolType]
public static class ScratchProbeTools
{
    [McpServerTool(Name = "scratch_write_probe", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Chunk 01 connectivity probe. Writes only to the fixed disposable write-probe.txt file in the Codicks Lite scratch directory. It cannot choose or modify any project path.")]
    public static Task<ScratchProbeResult> ScratchWriteProbe(
        ScratchProbeService scratchProbeService,
        ISessionGuard sessionGuard,
        [Description("Text to place in the disposable probe file. Maximum 4096 characters.")] string content,
        CancellationToken cancellationToken)
    {
        SessionAuthorization.RequireMutation(sessionGuard);
        return scratchProbeService.WriteProbeAsync(content, cancellationToken);
    }

    [McpServerTool(Name = "scratch_read_probe", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reads the fixed disposable Chunk 01 write-probe.txt file, if it exists. Does not accept a path.")]
    public static async Task<ScratchReadProbeResponse> ScratchReadProbe(
        ScratchProbeService scratchProbeService,
        ISessionGuard sessionGuard,
        CancellationToken cancellationToken)
    {
        SessionAuthorization.RequireFull(sessionGuard);
        var content = await scratchProbeService.ReadProbeAsync(cancellationToken);
        return new ScratchReadProbeResponse(
            content is not null,
            content);
    }
}
