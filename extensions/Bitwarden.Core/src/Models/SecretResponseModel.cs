namespace Bitwarden.Core.Models;

public class SecretResponseModel
{
    public SecretResponseModel(Guid id, EncryptedString key, EncryptedString value, DateTime revisionDate)
    {
        Id = id;
        Key = key;
        Value = value;
        RevisionDate = revisionDate;
    }

    public Guid Id { get; }
    public EncryptedString Key { get; }
    public EncryptedString Value { get; }
    public DateTime RevisionDate { get; }
}
