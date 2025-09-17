using LaunchDarkly.Sdk;

namespace Bitwarden.Server.Sdk.Features;

internal sealed class AnonymousContextBuilder : IContextBuilder
{
    private const string AnonymousUser = "25a15cac-58cf-4ac0-ad0f-b17c4bd92294";

    public Context Build()
    {
        return Context.Builder(ContextKind.Default, AnonymousUser)
            .Build();
    }
}
