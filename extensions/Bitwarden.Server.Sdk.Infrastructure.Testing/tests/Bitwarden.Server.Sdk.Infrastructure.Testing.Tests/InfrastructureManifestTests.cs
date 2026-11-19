namespace Bitwarden.Server.Sdk.Infrastructure.Testing.Tests;

public sealed class InfrastructureManifestTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"infrastructure-snapshots-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ToJsonString_WritesEnvelope()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = true);

        Assert.Equal(
            """{"schemaVersion":1,"selfHosted":true,"selfHostFlavor":"unknown","environment":"Production","requirements":{"database":[{"name":"vault"}],"messaging-subscription":[{"topic":"orders","subscription":"billing"}],"messaging-topic":[{"topic":"orders"}],"self-signed-certificate":[{"name":"identity"}]}}""",
            manifest.ToJsonString(indented: false));
    }

    [Fact]
    public void ToJsonString_DoesNotIncludeConfiguration()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
            o.Configuration["Sample:Secret"] = "do-not-write");

        Assert.DoesNotContain("do-not-write", manifest.ToJsonString());
    }

    [Fact]
    public void ToJsonString_Indented_UsesLineFeeds()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();

        var json = manifest.ToJsonString();

        Assert.Contains("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void VerifySnapshot_Missing_CreatesSnapshotAndThrows()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
        var path = Path.Combine(_directory, "nested", "infrastructure.cloud.json");

        var exception = Assert.Throws<InfrastructureSnapshotMismatchException>(() => manifest.VerifySnapshot(path));

        Assert.True(exception.Created);
        Assert.Equal(path, exception.SnapshotPath);
        Assert.Equal(manifest.ToJsonString() + "\n", File.ReadAllText(path));
    }

    [Fact]
    public void VerifySnapshot_UpToDate_DoesNotThrowOrRewrite()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
        var path = WriteSnapshot("infrastructure.cloud.json", manifest.ToJsonString() + "\n");
        var lastWrite = File.GetLastWriteTimeUtc(path);

        manifest.VerifySnapshot(path);

        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void VerifySnapshot_WithCarriageReturns_StillMatches()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
        var path = WriteSnapshot("infrastructure.cloud.json", (manifest.ToJsonString() + "\n").ReplaceLineEndings("\r\n"));

        manifest.VerifySnapshot(path);
    }

    [Fact]
    public void VerifySnapshot_OutOfDate_UpdatesSnapshotAndThrows()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
        var path = WriteSnapshot("infrastructure.cloud.json", "{}\n");

        var exception = Assert.Throws<InfrastructureSnapshotMismatchException>(() => manifest.VerifySnapshot(path));

        Assert.False(exception.Created);
        Assert.Equal(manifest.ToJsonString() + "\n", File.ReadAllText(path));
    }

    [Fact]
    public void VerifySnapshot_RelativePath_IsResolvedAgainstCallerFile()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();
        var callerFile = Path.Combine(_directory, "Tests", "InfrastructureTests.cs");

        Assert.Throws<InfrastructureSnapshotMismatchException>(() =>
            manifest.VerifySnapshot(Path.Combine("snapshots", "infrastructure.cloud.json"), callerFile));

        Assert.True(File.Exists(Path.Combine(_directory, "Tests", "snapshots", "infrastructure.cloud.json")));
    }

    [Fact]
    public void VerifySnapshot_EmptyPath_Throws()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.Throws<ArgumentException>(() => manifest.VerifySnapshot(""));
    }

    private string WriteSnapshot(string fileName, string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
