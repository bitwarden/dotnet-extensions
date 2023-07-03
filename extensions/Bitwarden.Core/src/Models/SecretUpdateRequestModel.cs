using System.Collections.ObjectModel;

namespace Bitwarden.Core.Models;

public sealed class SecretUpdateRequestModel
{
    // TODO: Note should not be string, I need a way to have an empty EncryptedString
    public SecretUpdateRequestModel(EncryptedString key, EncryptedString value, string note, Guid? projectId)
    {
        Key = key;
        Value = value;
        Note = note;
        ProjectIds = projectId.HasValue ? new ReadOnlyCollection<Guid>(new [] { projectId.Value }) : Array.Empty<Guid>();
    }

    public EncryptedString Key { get; }
    public EncryptedString Value { get; }
    public string Note { get; }
    public IReadOnlyList<Guid> ProjectIds { get; }
}
