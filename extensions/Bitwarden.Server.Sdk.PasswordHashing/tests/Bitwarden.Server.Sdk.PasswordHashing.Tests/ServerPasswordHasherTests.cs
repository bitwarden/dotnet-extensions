using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.PasswordHashing.Tests;

public sealed class ServerPasswordHasherTests
{
    private const string ClientHash = "r5CFRR+n9NQI8a525FY+0BPR0HGOjVJX0cR1KEMnIOo=";

    // V3 layout: marker, PRF, iterations, salt length, salt, subkey. Integers are big-endian.
    //   [0x01][prf:4][iter:4][saltLen:4][salt:16][subkey:32]
    private const byte V3Marker = 0x01;
    private const int Sha512Prf = 2;
    private const int ServerIterations = 100_000;
    private const int SaltLength = 16;
    private const int SubkeyLength = 32;
    private const int HeaderLength = 13;

    private readonly IServerPasswordHasher _hasher = CreateHasher(new ServiceCollection());

    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        var stored = _hasher.Hash(ClientHash);

        Assert.Equal(PasswordHashVerification.Success, _hasher.Verify(stored, ClientHash));
    }

    [Fact]
    public void Hash_IsV3Pbkdf2Sha512With100kIterations()
    {
        var stored = Convert.FromBase64String(_hasher.Hash(ClientHash));

        Assert.Equal(HeaderLength + SaltLength + SubkeyLength, stored.Length);
        Assert.Equal(V3Marker, stored[0]);
        Assert.Equal(Sha512Prf, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(1)));
        Assert.Equal(ServerIterations, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(5)));
        Assert.Equal(SaltLength, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(9)));

        // Recompute the subkey without ASP.NET Identity to pin the algorithm.
        var salt = stored.AsSpan(HeaderLength, SaltLength).ToArray();
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(ClientHash), salt, ServerIterations, HashAlgorithmName.SHA512, SubkeyLength);
        Assert.Equal(subkey, stored.AsSpan(HeaderLength + SaltLength).ToArray());
    }

    [Fact]
    public void Hash_SameClientHash_UsesFreshSalt()
    {
        Assert.NotEqual(_hasher.Hash(ClientHash), _hasher.Hash(ClientHash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Hash_MissingClientHash_Throws(string? clientHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.Hash(clientHash!));
    }

    [Fact]
    public void Verify_HashBuiltFromLayout_Succeeds()
    {
        var stored = BuildV3(ClientHash, ServerIterations);

        Assert.Equal(PasswordHashVerification.Success, _hasher.Verify(stored, ClientHash));
    }

    [Fact]
    public void Verify_HashStoredByServer_Succeeds()
    {
        // The server hashes master password and Send password hashes with ASP.NET Identity, V3, 100k.
        var server = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions { IterationCount = ServerIterations }));
        var stored = server.HashPassword(new object(), ClientHash);

        Assert.Equal(PasswordHashVerification.Success, _hasher.Verify(stored, ClientHash));
    }

    [Fact]
    public void Hash_VerifiedByServer_Succeeds()
    {
        var server = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions { IterationCount = ServerIterations }));

        var result = server.VerifyHashedPassword(new object(), _hasher.Hash(ClientHash), ClientHash);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_FewerIterations_AsksForRehash()
    {
        var stored = BuildV3(ClientHash, iterations: 10_000);

        Assert.Equal(PasswordHashVerification.SuccessRehashNeeded, _hasher.Verify(stored, ClientHash));
    }

    [Fact]
    public void Verify_V2Hash_AsksForRehash()
    {
        var v2 = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        }));
        var stored = v2.HashPassword(new object(), ClientHash);

        Assert.Equal(PasswordHashVerification.SuccessRehashNeeded, _hasher.Verify(stored, ClientHash));
    }

    [Fact]
    public void Verify_WrongClientHash_Fails()
    {
        var stored = _hasher.Hash(ClientHash);

        Assert.Equal(PasswordHashVerification.Failed, _hasher.Verify(stored, "wrong"));
    }

    [Theory]
    [InlineData(null, ClientHash)]
    [InlineData("", ClientHash)]
    [InlineData(" ", ClientHash)]
    [InlineData("AQ==", null)]
    [InlineData("AQ==", "")]
    [InlineData("AQ==", " ")]
    public void Verify_MissingValue_Fails(string? storedHash, string? clientHash)
    {
        Assert.Equal(PasswordHashVerification.Failed, _hasher.Verify(storedHash, clientHash));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AQ==")]
    public void Verify_MalformedStoredHash_Fails(string storedHash)
    {
        Assert.Equal(PasswordHashVerification.Failed, _hasher.Verify(storedHash, ClientHash));
    }

    [Fact]
    public void Hash_HostTunesIdentity_KeepsServerParameters()
    {
        var services = new ServiceCollection();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 1);
        var hasher = CreateHasher(services);

        var stored = Convert.FromBase64String(hasher.Hash(ClientHash));

        Assert.Equal(ServerIterations, BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(5)));
    }

    private static IServerPasswordHasher CreateHasher(IServiceCollection services) =>
        services.AddServerPasswordHasher().BuildServiceProvider().GetRequiredService<IServerPasswordHasher>();

    private static string BuildV3(string clientHash, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(clientHash), salt, iterations, HashAlgorithmName.SHA512, SubkeyLength);

        var output = new byte[HeaderLength + SaltLength + SubkeyLength];
        output[0] = V3Marker;
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(1), Sha512Prf);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(5), iterations);
        BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(9), SaltLength);
        salt.CopyTo(output, HeaderLength);
        subkey.CopyTo(output, HeaderLength + SaltLength);

        return Convert.ToBase64String(output);
    }
}
