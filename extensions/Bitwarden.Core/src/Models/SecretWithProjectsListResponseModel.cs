namespace Bitwarden.Core.Models;

public sealed class SecretWithProjectsListResponseModel
{
    public SecretWithProjectsListResponseModel(IAsyncEnumerable<Secret> secrets, IAsyncEnumerable<Project> projects)
    {
        Secrets = secrets;
        Projects = projects;
    }

    public IAsyncEnumerable<Secret> Secrets { get; }
    public IAsyncEnumerable<Project> Projects { get; }

    public sealed class Secret
    {
        public Secret(Guid id, DateTime revisionDate, EncryptedString key)
        {
            Id = id;
            RevisionDate = revisionDate;
            Key = key;
        }

        public Guid Id { get; }
        public DateTime RevisionDate { get; }
        public EncryptedString Key { get; }
    }

    public sealed class Project
    {

    }
}
