namespace Bitwarden.Core.Models;

public class ProjectResponseModel
{
    public ProjectResponseModel(
        Guid id,
        Guid organizationId,
        string name,
        DateTime creationDate,
        DateTime revisionDate,
        bool read,
        bool write)
    {
        (Id, OrganizationId, Name, CreationDate, RevisionDate, Read, Write) 
            = (id, organizationId, name, creationDate, revisionDate, read, write);
    }
    public Guid Id { get; }
    public Guid OrganizationId { get; }
    public string Name { get; }
    public DateTime CreationDate { get; }
    public DateTime RevisionDate { get; }
    public bool Read { get; }
    public bool Write { get; }
}
