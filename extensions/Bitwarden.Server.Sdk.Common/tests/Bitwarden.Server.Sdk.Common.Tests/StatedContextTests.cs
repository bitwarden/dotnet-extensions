namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class StatedContextTests
{
    [Fact]
    public void Members_AnswerWhatTheyWereGiven()
    {
        var organization = Guid.NewGuid();
        var user = Guid.NewGuid();

        IContext context = new StatedContext
        {
            OrganizationId = organization,
            UserId = user,
            Credential = CallerCredential.System,
            UserOrganizationIds = [organization],
        };

        Assert.Equal(organization, context.OrganizationId);
        Assert.Equal(user, context.UserId);
        Assert.Null(context.ProviderId);
        Assert.Equal(CallerCredential.System, context.Credential);
        Assert.Equal([organization], context.UserOrganizationIds);
    }
}
