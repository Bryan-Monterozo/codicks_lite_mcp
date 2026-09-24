using LocalAgent.Core.Audit;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Execution;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Core.Recovery;
using LocalAgent.Core.Security;
using LocalAgent.Core.Workspaces;
using LocalAgent.Host.Configuration;
using LocalAgent.Infrastructure.Audit;
using LocalAgent.Infrastructure.Execution;
using LocalAgent.Infrastructure.FileSystem;
using LocalAgent.Infrastructure.Files;
using LocalAgent.Infrastructure.Paths;
using LocalAgent.Infrastructure.Recovery;
using LocalAgent.Infrastructure.Scratch;
using LocalAgent.Infrastructure.Security;
using LocalAgent.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

var configurationRuntimeInfo = ConfigurationBootstrap.Configure(builder.Configuration);

// stdio is the MCP transport. Never write application logs to stdout.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var configuredLogLevel = Enum.TryParse<LogLevel>(
    builder.Configuration["Agent:Logging:MinimumLevel"],
    ignoreCase: true,
    out var parsedLogLevel)
        ? parsedLogLevel
        : LogLevel.Information;

builder.Logging.SetMinimumLevel(configuredLogLevel);

builder.Services.AddSingleton(configurationRuntimeInfo);
builder.Services.AddSingleton<IUserPathResolver, UserPathResolver>();
builder.Services.AddSingleton<WorkspaceTopologyValidator>();
builder.Services.AddSingleton<IValidateOptions<AgentConfiguration>, AgentConfigurationOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<AgentConfiguration>, WorkspaceTopologyOptionsValidator>();

builder.Services
    .AddOptions<AgentConfiguration>()
    .Bind(builder.Configuration)
    .ValidateOnStart();

builder.Services.AddSingleton<AgentConfiguration>(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<AgentConfiguration>>().Value);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISessionOtpGenerator, SessionOtpGenerator>();
builder.Services.AddSingleton<ISessionGuard, SessionGuard>();

builder.Services.AddSingleton<IWorkspaceRegistry>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IOptions<AgentConfiguration>>().Value;
    var pathResolver = serviceProvider.GetRequiredService<IUserPathResolver>();
    return new WorkspaceRegistry(configuration, pathResolver);
});

builder.Services.AddSingleton<IWorkspaceResolver, WorkspaceResolver>();
builder.Services.AddSingleton<IWorkspacePermissionEvaluator>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IOptions<AgentConfiguration>>().Value;
    return new WorkspacePermissionEvaluator(configuration);
});

builder.Services.AddSingleton<IDenyPathMatcher>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IOptions<AgentConfiguration>>().Value;
    var denyGlobs = AgentSecurityDefaults.GetEffectiveDenyGlobs(configuration.Agent.Security.DenyGlobs);
    return new DenyPathMatcher(denyGlobs);
});

builder.Services.AddSingleton<IFileSystemEntryInspector, MacOsFileSystemEntryInspector>();
builder.Services.AddSingleton<IWorkspacePathPolicy, WorkspacePathPolicy>();

// Local command execution policy/runners.
builder.Services.AddSingleton<IExecutablePolicy, ExecutablePolicy>();
builder.Services.AddSingleton<HostProcessExecutionService>();
builder.Services.AddSingleton<SandboxProcessExecutionService>();
builder.Services.AddSingleton<IProcessExecutionService, ProcessExecutionRouter>();

// Chunk 04 read-only service.
builder.Services.AddSingleton<IWorkspaceQueryService, WorkspaceQueryService>();

// Chunk 05 create/update services.
builder.Services.AddSingleton<IFileHasher, Sha256FileHasher>();
builder.Services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
builder.Services.AddSingleton<IUpdateBackupStore, UpdateBackupStore>();
builder.Services.AddSingleton<IWorkspaceMutationService, WorkspaceMutationService>();

// Chunk 06 lifecycle/recovery services.
builder.Services.AddSingleton<IFileSystemDeviceInspector, MacOsFileSystemDeviceInspector>();
builder.Services.AddSingleton<WorkspaceRecoveryLocationResolver>();
builder.Services.AddSingleton<IRecoveryStore, FileRecoveryStore>();
builder.Services.AddSingleton<IMutationReceiptStore, JsonMutationReceiptStore>();
builder.Services.AddSingleton<IWorkspaceLifecycleService, WorkspaceLifecycleService>();

builder.Services.AddSingleton<IAuditWriter, JsonLinesAuditWriter>();

builder.Services.AddHostedService<ConfigurationStartupService>();
builder.Services.AddHostedService<UnixControlSocketServer>();

// Chunk 01 connectivity probes remain available and unchanged.
builder.Services.AddSingleton<ScratchProbeService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
