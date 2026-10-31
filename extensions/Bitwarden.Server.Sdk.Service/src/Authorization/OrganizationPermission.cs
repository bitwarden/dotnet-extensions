namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// One permission a custom member may hold.
/// </summary>
/// <remarks>
/// The set a fixed role implies is not modelled: an owner or an administrator satisfies a
/// permission requirement by their role, and only a <see cref="OrganizationRole.Custom"/> member
/// is asked which permissions they hold. Inverting that would mean deciding, here in the
/// platform, which permissions each role implies.
/// </remarks>
public enum OrganizationPermission
{
    /// <summary>
    /// No permission required.
    /// </summary>
    None = 0,

    /// <summary>
    /// Read the organization's event log.
    /// </summary>
    AccessEventLogs,

    /// <summary>
    /// Import into and export out of the organization.
    /// </summary>
    AccessImportExport,

    /// <summary>
    /// Read the organization's reports.
    /// </summary>
    AccessReports,

    /// <summary>
    /// Create collections.
    /// </summary>
    CreateNewCollections,

    /// <summary>
    /// Edit any collection, not only those granted to them.
    /// </summary>
    EditAnyCollection,

    /// <summary>
    /// Delete any collection, not only those granted to them.
    /// </summary>
    DeleteAnyCollection,

    /// <summary>
    /// Administer groups.
    /// </summary>
    ManageGroups,

    /// <summary>
    /// Administer policies.
    /// </summary>
    ManagePolicies,

    /// <summary>
    /// Administer single sign-on.
    /// </summary>
    ManageSso,

    /// <summary>
    /// Administer members.
    /// </summary>
    ManageUsers,

    /// <summary>
    /// Recover a member's account.
    /// </summary>
    ManageResetPassword,

    /// <summary>
    /// Administer directory provisioning.
    /// </summary>
    ManageScim,

    /// <summary>
    /// Administer access rules.
    /// </summary>
    ManageAccessRules,
}
