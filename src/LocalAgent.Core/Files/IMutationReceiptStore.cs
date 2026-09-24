namespace LocalAgent.Core.Files;

public interface IMutationReceiptStore
{
    PersistentMutationReceipt? Load(string mutationId);

    void Create(PersistentMutationReceipt receipt);

    void Update(PersistentMutationReceipt receipt);
}
