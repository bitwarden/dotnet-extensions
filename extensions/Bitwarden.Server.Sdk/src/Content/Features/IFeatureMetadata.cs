#pragma warning disable BWSDK0001
#nullable enable

using System.ComponentModel;
using Bitwarden.Server.Sdk.Utilities.Internal;

namespace Bitwarden.Server.Sdk.Features;


[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete(InternalConstants.InternalMessage, DiagnosticId = InternalConstants.InternalId)]
internal interface IFeatureMetadata
{
    /// <summary>
    /// A method to run to check if the feature is enabled.
    /// </summary>
    Func<IFeatureService, bool> FeatureCheck { get; set; }
}
