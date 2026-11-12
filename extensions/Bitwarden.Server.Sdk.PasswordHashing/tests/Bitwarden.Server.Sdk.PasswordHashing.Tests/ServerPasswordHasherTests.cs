using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.PasswordHashing.Tests;

public sealed class ServerPasswordHasherTests
{
    private const string ClientValue = "r5CFRR+n9NQI8a525FY+0BPR0HGOjVJX0cR1KEMnIOo=";
    private static readonly ClientCreatedHash Client = new(ClientValue);

    // V3 layout: marker, PRF, iterations, salt length, salt, subkey. Integers are big-endian.
    //   [0x01][prf:4][iter:4][saltLen:4][salt:16][subkey:32]
    private const byte V3Marker = 0x01;
    private const int Sha512Prf = 2;
    private const int ServerIterations = 100_000;
    private const int SaltLength = 16;
    private const int SubkeyLength = 32;
    private const int HeaderLength = 13;

    // Hashes of ClientValue produced by the Bitwarden server's own registrations
    // (AddCustomIdentityServices for IPasswordHasher<User>, AddSendPasswordServices for Sends).
    private const string ServerMasterPasswordHash =
        "AQAAAAIAAYagAAAAEHkSHtEb9vUEGyeqeEWfvai8k2rNVxb46H1J4bwc/a1fLkV4B4luKD3tyQjlNp7uTw==";
    private const string ServerSendPasswordHash =
        "AQAAAAIAAYagAAAAEJ2PKNVcsvfAKEuo4cZBX1sC4++C7nOhANUcHT6kGEtebRiCxNDEBSAkny5IGBGqow==";

    // Same, with the 25,000 iterations the server used from 2018 until it moved to 100,000.
    private const string ServerLegacyHash =
        "AQAAAAIAAGGoAAAAEHI1cg5ZNKVNQA0RSk5aKhHpBDUt3u6g6Uyh4qQtnNsj1cFK+Ls2DgPeVWiuBJn5Og==";

    private readonly IServerPasswordHasher _hasher = CreateHasher(new ServiceCollection());

    [Fact]
    public void CreateStoredHash_ThenVerify_Succeeds()
    {
        var stored = _hasher.CreateStoredHash(Client);

        Assert.Equal(PasswordHashVerification.Success, _hasher.VerifyStoredHash(stored, Client));
    }

    [Fact]
    public void CreateStoredHash_IsV3Pbkdf2Sha512With100kIterations()
    {
        var stored = Convert.FromBase64String(_hasher.CreateStoredHash(Client).Value);

        Assert.Equal(HeaderLength + SaltLength + SubkeyLength, stored.Length);
        Assert.Equal(V3Marker, stored[0]);
        Assert.Equal(Sha512Prf, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(1)));
        Assert.Equal(ServerIterations, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(5)));
        Assert.Equal(SaltLength, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(9)));

        // Recompute the subkey without ASP.NET Identity to pin the algorithm.
        var salt = stored.AsSpan(HeaderLength, SaltLength).ToArray();
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(ClientValue), salt, ServerIterations, HashAlgorithmName.SHA512, SubkeyLength);
        Assert.Equal(subkey, stored.AsSpan(HeaderLength + SaltLength).ToArray());
    }

    [Fact]
    public void CreateStoredHash_SameClientHash_UsesFreshSalt()
    {
        Assert.NotEqual(_hasher.CreateStoredHash(Client), _hasher.CreateStoredHash(Client));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateStoredHash_MissingClientHash_Throws(string? clientHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.CreateStoredHash(new(clientHash!)));
    }

    [Fact]
    public void CreateStoredHash_DefaultClientHash_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.CreateStoredHash(default));
    }

    [Theory]
    [InlineData(ServerMasterPasswordHash)]
    [InlineData(ServerSendPasswordHash)]
    public void VerifyStoredHash_HashStoredByServer_Succeeds(string stored)
    {
        Assert.Equal(PasswordHashVerification.Success, _hasher.VerifyStoredHash(new(stored), Client));
    }

    [Fact]
    public void CreateStoredHash_VerifiedByServer_Succeeds()
    {
        var server = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions { IterationCount = ServerIterations }));

        var result = server.VerifyHashedPassword(new object(), _hasher.CreateStoredHash(Client).Value, ClientValue);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void VerifyStoredHash_LegacyServerHash_AsksForRehash()
    {
        Assert.Equal(PasswordHashVerification.SuccessRehashNeeded, _hasher.VerifyStoredHash(new(ServerLegacyHash), Client));
    }

    [Fact]
    public void VerifyStoredHash_V2Hash_AsksForRehash()
    {
        var v2 = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        }));
        var stored = v2.HashPassword(new object(), ClientValue);

        Assert.Equal(PasswordHashVerification.SuccessRehashNeeded, _hasher.VerifyStoredHash(new(stored), Client));
    }

    [Fact]
    public void VerifyStoredHash_WrongClientHash_Fails()
    {
        var stored = _hasher.CreateStoredHash(Client);

        Assert.Equal(PasswordHashVerification.Failed, _hasher.VerifyStoredHash(stored, new("wrong")));
    }

    [Theory]
    [InlineData(null, ClientValue)]
    [InlineData("", ClientValue)]
    [InlineData(" ", ClientValue)]
    [InlineData("AQ==", null)]
    [InlineData("AQ==", "")]
    [InlineData("AQ==", " ")]
    public void VerifyStoredHash_MissingValue_Fails(string? storedHash, string? clientHash)
    {
        Assert.Equal(PasswordHashVerification.Failed, _hasher.VerifyStoredHash(new(storedHash!), new(clientHash!)));
    }

    [Fact]
    public void VerifyStoredHash_DefaultWrappers_Fails()
    {
        Assert.Equal(PasswordHashVerification.Failed, _hasher.VerifyStoredHash(default, default));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AQ==")]
    public void VerifyStoredHash_MalformedStoredHash_Fails(string storedHash)
    {
        Assert.Equal(PasswordHashVerification.Failed, _hasher.VerifyStoredHash(new(storedHash), Client));
    }

    [Fact]
    public void CreateStoredHash_HostTunesIdentity_KeepsServerParameters()
    {
        var services = new ServiceCollection();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 1);
        var hasher = CreateHasher(services);

        var stored = Convert.FromBase64String(hasher.CreateStoredHash(Client).Value);

        Assert.Equal(ServerIterations, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(5)));
    }

    private static IServerPasswordHasher CreateHasher(IServiceCollection services) =>
        services.AddServerPasswordHasher().BuildServiceProvider().GetRequiredService<IServerPasswordHasher>();
}
