using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAgent.Core.Configuration;
using LocalAgent.Core.Files;
using LocalAgent.Core.Paths;
using LocalAgent.Infrastructure.Files;

namespace LocalAgent.Infrastructure.Recovery;

public sealed class JsonMutationReceiptStore(
    AgentConfiguration configuration,
    IUserPathResolver pathResolver) : IMutationReceiptStore
{
    private readonly string _root = Path.Combine(
        pathResolver.Resolve(configuration.Agent.StateDirectory),
        "mutation-receipts");

    public PersistentMutationReceipt? Load(string mutationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutationId);

        var path = GetPath(mutationId);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<PersistentMutationReceipt>(bytes)
            ?? throw new IOException("Mutation receipt metadata is invalid.");
    }

    public void Create(PersistentMutationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Directory.CreateDirectory(_root);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        DurableFilePersistence.CreateNew(
            GetPath(receipt.MutationId),
            bytes,
            unixFileMode: null);
    }

    public void Update(PersistentMutationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Directory.CreateDirectory(_root);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        DurableFilePersistence.ReplaceExisting(
            GetPath(receipt.MutationId),
            bytes,
            unixFileMode: null);
    }

    private string GetPath(string mutationId)
    {
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(mutationId)));

        return Path.Combine(_root, $"{key}.json");
    }

}
