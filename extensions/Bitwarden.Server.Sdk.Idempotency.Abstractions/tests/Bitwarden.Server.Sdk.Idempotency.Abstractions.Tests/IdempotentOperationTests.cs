using Bitwarden.Server.Sdk.CryptoPrimitives;

namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class IdempotentOperationTests
{
    private const string Address = "https://callee/api/v1/things";

    // A test secret, 32 bytes of 0x01, which the golden vectors below are computed with.
    private static readonly CacheSecret Secret = new(Enumerable.Repeat((byte)1, CacheSecret.Length).ToArray());

    [Fact]
    public void OutsideAnOperation_NothingIsDerived()
    {
        Assert.Null(IdempotentOperation.Current);
        Assert.Null(IdempotentOperation.KeyForWrite("POST", Address, "{}"u8));
        Assert.Null(IdempotentOperation.DeriveId("purpose"));
    }

    // A run again from the start sends the same keys and names the same things.
    [Fact]
    public void ARunAgain_DerivesTheSameKeysAndIds()
    {
        static (string?[] Keys, Guid? Id) Run()
        {
            using var operation = IdempotentOperation.Begin("operation-1", Secret);
            return (
                [
                    IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8),
                    IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8),
                    IdempotentOperation.KeyForWrite("DELETE", Address, []),
                ],
                IdempotentOperation.DeriveId("mail"));
        }

        var first = Run();
        var again = Run();

        Assert.Equal(first.Keys, again.Keys);
        Assert.Equal(3, first.Keys.Distinct().Count());
        Assert.Equal(first.Id, again.Id);
        Assert.Null(IdempotentOperation.Current);
    }

    // Writes to one address with different bodies keep their own keys whatever order a run makes them in, so a run
    // that reads its work in another order still repeats each write's key.
    [Fact]
    public void WritesToOneAddress_KeyedByTheirBodies_NotTheirOrder()
    {
        string?[] Run(params string[] bodies)
        {
            using var operation = IdempotentOperation.Begin("operation-1", Secret);
            return [.. bodies.Select(body => IdempotentOperation.KeyForWrite("POST", Address, System.Text.Encoding.UTF8.GetBytes(body)))];
        }

        var first = Run("a", "b");
        var swapped = Run("b", "a");

        Assert.Equal(first[0], swapped[1]);
        Assert.Equal(first[1], swapped[0]);
    }

    // A write whose body differs from the first run's is a new write with a key of its own, never refused for differing.
    [Fact]
    public void AWriteWithAnotherBody_GetsAnotherKey()
    {
        string? Run(string body)
        {
            using var operation = IdempotentOperation.Begin("operation-1", Secret);
            return IdempotentOperation.KeyForWrite("PUT", Address, System.Text.Encoding.UTF8.GetBytes(body));
        }

        Assert.NotEqual(Run("one"), Run("two"));
    }

    [Fact]
    public void AnotherOperation_DerivesOtherKeysAndIds()
    {
        (string? Key, Guid? Id) Run(string key)
        {
            using var operation = IdempotentOperation.Begin(key, Secret);
            return (IdempotentOperation.KeyForWrite("POST", Address, []), IdempotentOperation.DeriveId("mail"));
        }

        var one = Run("operation-1");
        var two = Run("operation-2");

        Assert.NotEqual(one.Key, two.Key);
        Assert.NotEqual(one.Id, two.Id);
    }

    [Fact]
    public void DeriveId_IsAVersion8Uuid_OnePerAsk()
    {
        using var operation = IdempotentOperation.Begin("operation-1", Secret);

        var first = IdempotentOperation.DeriveId("mail")!.Value;
        var second = IdempotentOperation.DeriveId("mail")!.Value;
        var other = IdempotentOperation.DeriveId("other")!.Value;

        Assert.Equal(8, first.Version);
        Assert.Equal('8', first.ToString()[14]);
        Assert.Contains(first.ToString()[19], "89ab");
        Assert.Equal(3, new[] { first, second, other }.Distinct().Count());
    }

    [Fact]
    public void Begin_Nested_RestoresTheOuterOperation()
    {
        using (IdempotentOperation.Begin("outer", Secret))
        {
            using (IdempotentOperation.Begin("inner", Secret))
            {
                Assert.Equal("inner", IdempotentOperation.Current);
            }

            Assert.Equal("outer", IdempotentOperation.Current);
        }

        Assert.Null(IdempotentOperation.Current);
    }

    // Ended out of order (the outer first), an operation that has ended is never current again.
    [Fact]
    public void Begin_EndedOutOfOrder_NeverBringsBackAnEndedOperation()
    {
        var outer = IdempotentOperation.Begin("outer-op", Secret);
        var inner = IdempotentOperation.Begin("inner-op", Secret);

        outer.Dispose();
        inner.Dispose();

        Assert.Null(IdempotentOperation.Current);
        Assert.Null(IdempotentOperation.KeyForWrite("POST", Address, []));
        Assert.Null(IdempotentOperation.DeriveId("mail"));
    }

    // Ending an operation twice restores the outer one only once, so an operation begun since stays current.
    [Fact]
    public void Begin_EndedTwice_RestoresTheOuterOnlyOnce()
    {
        using var outer = IdempotentOperation.Begin("outer-op", Secret);
        var inner = IdempotentOperation.Begin("inner-op", Secret);
        inner.Dispose();

        using var next = IdempotentOperation.Begin("next-op", Secret);
        inner.Dispose();

        Assert.Equal("next-op", IdempotentOperation.Current);
    }

    // An operation ended out of order leaves alone one begun since: ending the one it was begun inside skips past it.
    [Fact]
    public void Begin_EndedOutOfOrder_KeepsAnOperationBegunSince()
    {
        var a = IdempotentOperation.Begin("a", Secret);
        var b = IdempotentOperation.Begin("b", Secret);
        a.Dispose();
        using var c = IdempotentOperation.Begin("c", Secret);

        b.Dispose();

        Assert.Equal("c", IdempotentOperation.Current);
    }

    // Ending inner operations out of order brings back the nearest outer one that is still running.
    [Fact]
    public void Begin_InnerOperationsEndedOutOfOrder_RestoreTheOuterOne()
    {
        using var outer = IdempotentOperation.Begin("outer-op", Secret);
        var first = IdempotentOperation.Begin("first-op", Secret);
        var second = IdempotentOperation.Begin("second-op", Secret);

        first.Dispose();
        Assert.Equal("second-op", IdempotentOperation.Current);

        second.Dispose();
        Assert.Equal("outer-op", IdempotentOperation.Current);
    }

    [Fact]
    public void Begin_AKeyOfTheMaximumLength_Begins()
    {
        using var operation = IdempotentOperation.Begin(new string('k', IdempotentOperation.MaximumKeyLength), Secret);

        Assert.Equal(IdempotentOperation.MaximumKeyLength, IdempotentOperation.Current?.Length);
    }

    // Longer than an operation key may be, though still a valid idempotency key.
    [Theory]
    [InlineData(IdempotentOperation.MaximumKeyLength + 1)]
    [InlineData(IdempotencyKeys.MaximumLength)]
    public void Begin_AKeyLongerThanTheMaximum_IsRefused(int length)
    {
        Assert.Throws<ArgumentException>(() => IdempotentOperation.Begin(new string('k', length), Secret));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has a space")]
    [InlineData("naïve")]
    public void Begin_AKeyThatIsNotVisibleAscii_IsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => IdempotentOperation.Begin(key, Secret));
    }

    [Fact]
    public void ForMessage_IsTheHandlerAndTheEnvelope()
    {
        var envelope = Guid.NewGuid();

        Assert.Equal($"Some.Handler:{envelope}", IdempotentOperation.ForMessage("Some.Handler", envelope));
    }

    [Fact]
    public void ForMessage_ALongName_IsShortenedToItsHash_AndStillBegins()
    {
        var envelope = Guid.NewGuid();
        var name = new string('h', 300);

        var key = IdempotentOperation.ForMessage(name, envelope);

        Assert.True(key.Length <= IdempotentOperation.MaximumKeyLength);
        Assert.EndsWith($":{envelope}", key, StringComparison.Ordinal);
        Assert.Equal(key, IdempotentOperation.ForMessage(name, envelope));
        using var operation = IdempotentOperation.Begin(key, Secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForMessage_ABlankName_IsRefused(string? handler)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotentOperation.ForMessage(handler!, Guid.NewGuid()));
    }

    [Fact]
    public void ForMessage_AStringIdentifier_IsTheHandlerAndTheIdentifier()
    {
        Assert.Equal("Some.Handler:device-1.0192", IdempotentOperation.ForMessage("Some.Handler", "device-1.0192"));
    }

    [Fact]
    public void ForMessage_AGuidsStringIdentifier_IsTheGuidsKey()
    {
        var envelope = Guid.CreateVersion7();

        Assert.Equal(
            IdempotentOperation.ForMessage("Some.Handler", envelope),
            IdempotentOperation.ForMessage("Some.Handler", envelope.ToString()));
        Assert.Equal(
            IdempotentOperation.ForMessage(new string('h', 300), envelope),
            IdempotentOperation.ForMessage(new string('h', 300), envelope.ToString()));
    }

    [Fact]
    public void ForMessage_TheLongestIdentifierWithALongName_StillBegins()
    {
        var key = IdempotentOperation.ForMessage(new string('h', 300), new string('m', IdempotentOperation.MaximumMessageIdLength));

        Assert.True(key.Length <= IdempotentOperation.MaximumKeyLength);
        using var operation = IdempotentOperation.Begin(key, Secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a message")]
    [InlineData("café")]
    public void ForMessage_AnIdentifierThatIsNotVisibleAscii_IsRefused(string? messageId)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotentOperation.ForMessage("Some.Handler", messageId!));
    }

    [Fact]
    public void ForMessage_AnIdentifierTooLong_IsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            IdempotentOperation.ForMessage("Some.Handler", new string('m', IdempotentOperation.MaximumMessageIdLength + 1)));
    }

    // Unnamed jobs would otherwise share one key per occurrence, and replay each other's writes.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForJob_ABlankName_IsRefused(string? job)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotentOperation.ForJob(job!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ForJob_IsTheJobAndTheOccurrence()
    {
        var dueAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        Assert.Equal("job:purge:1700000000000", IdempotentOperation.ForJob("purge", dueAt));
        Assert.NotEqual(IdempotentOperation.ForJob("purge", dueAt), IdempotentOperation.ForJob("purge", dueAt.AddDays(1)));
    }

    [Fact]
    public void KeyForWrite_ANullMethodOrAddress_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => IdempotentOperation.KeyForWrite(null!, Address, []));
        Assert.Throws<ArgumentNullException>(() => IdempotentOperation.KeyForWrite("POST", null!, []));
    }

    // A field that holds a newline can't pass for two fields: each is its own element of the tuple. Each write is the
    // first of its operation, so only the fields tell the keys apart.
    [Fact]
    public void KeyForWrite_AMethodAndAddressAreNeverAmbiguous()
    {
        string? Run(string method, string address)
        {
            using var operation = IdempotentOperation.Begin("operation-1", Secret);
            return IdempotentOperation.KeyForWrite(method, address, []);
        }

        Assert.NotEqual(Run("POST", "https://callee/a\nhttps://callee/b"), Run("POST\nhttps://callee/a", "https://callee/b"));
    }

    // Identical writes are counted by their fields, never by a joined string, so a write whose fields join to the same
    // text as an earlier write's is still the first of its kind.
    [Fact]
    public void KeyForWrite_CountsIdenticalWritesByTheirFields()
    {
        string? Run(bool first)
        {
            using var operation = IdempotentOperation.Begin("operation-1", Secret);
            if (first)
            {
                IdempotentOperation.KeyForWrite("POST", "https://callee/a\nhttps://callee/b", []);
            }

            return IdempotentOperation.KeyForWrite("POST\nhttps://callee/a", "https://callee/b", []);
        }

        Assert.Equal(Run(first: false), Run(first: true));
    }

    // Golden vectors computed outside .NET, so a change that derives a different key for the same write fails here: a
    // run after an upgrade that derives other keys does its writes twice. CacheKey is HMAC-SHA-256 over each parameter
    // length-prefixed; computed with Python's hmac:
    //
    //   import base64, hashlib, hmac, struct, uuid
    //
    //   def derive(secret, *params):
    //       data = b"".join(struct.pack(">Q", len(p.encode())) + p.encode() for p in params)
    //       return hmac.new(secret, data, hashlib.sha256).digest()
    //
    //   def write_key(secret, op, method, address, body, ordinal):
    //       return derive(secret, "idempotency-key", op, method, address, base64.b64encode(body).decode(),
    //                     str(ordinal)).hex()
    //
    //   def derive_id(secret, op, purpose, ordinal):
    //       b = bytearray(derive(secret, "derived-id", op, purpose, str(ordinal))[:16])
    //       b[6] = (b[6] & 0x0F) | 0x80
    //       b[8] = (b[8] & 0x3F) | 0x80
    //       return uuid.UUID(bytes=bytes(b))
    //
    //   write_key(bytes([1]) * 32, "operation-1", "POST", "https://callee/api/v1/things", b'{"a":1}', 0)
    [Fact]
    public void KeyForWrite_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1", Secret);

        Assert.Equal(
            "09d3c98cc4a66200112729e26948214a5f43541e7b215891d4f382524fe2e371",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "d6aaa92d20bfc83545490d84a04866ba50819e3ed7d3e635489c6c7ad2a81257",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "e8a1c8020e4a51c1b5806bc57e6d702b790595062692961c0b47bc30e8b7bc47",
            IdempotentOperation.KeyForWrite("DELETE", Address, []));
    }

    // Computed with write_key above and bytes(32), the development secret.
    [Fact]
    public void KeyForWrite_WithTheDevelopmentSecret_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1", CacheSecret.Development);

        Assert.Equal(
            "348129896b11e0c49df43fb09bfedc69e5497733728841064e4297067818a157",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "e89e083351cf1733e683dbed192780931f9a81c3342b6399f80e2892aa45ca78",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "c798a9ea483104db7cb531e2b7cfe8bca02d2eb56201136013b0417e0c7eac4c",
            IdempotentOperation.KeyForWrite("DELETE", Address, []));
    }

    // Computed with derive_id above.
    [Fact]
    public void DeriveId_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1", Secret);

        Assert.Equal(Guid.Parse("7979dfd0-6f07-8e5c-aab1-8e2f1a7168fe"), IdempotentOperation.DeriveId("mail"));
        Assert.Equal(Guid.Parse("4a16d020-83f5-8209-b270-67ee3b0f4f77"), IdempotentOperation.DeriveId("mail"));
    }

    // Computed with derive_id above and bytes(32), the development secret.
    [Fact]
    public void DeriveId_WithTheDevelopmentSecret_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1", CacheSecret.Development);

        Assert.Equal(Guid.Parse("696037ed-5f96-888c-8e1d-af430c918e66"), IdempotentOperation.DeriveId("mail"));
        Assert.Equal(Guid.Parse("b380fe9f-4669-8a46-953f-732b3c909243"), IdempotentOperation.DeriveId("mail"));
    }

    // The secret keys every derivation: the same operation under another secret derives other keys and identifiers,
    // so only a holder of the secret can compute them from the operation's key.
    [Fact]
    public void AnotherSecret_DerivesOtherKeysAndIds()
    {
        static (string? Key, Guid? Id) Run(CacheSecret secret)
        {
            using var operation = IdempotentOperation.Begin("operation-1", secret);
            return (IdempotentOperation.KeyForWrite("POST", Address, "{}"u8), IdempotentOperation.DeriveId("mail"));
        }

        var mine = Run(Secret);
        var other = Run(new CacheSecret(Enumerable.Repeat((byte)2, CacheSecret.Length).ToArray()));

        Assert.NotEqual(mine.Key, other.Key);
        Assert.NotEqual(mine.Id, other.Id);
    }

    // Strict UTF-8 refuses a lone surrogate rather than replacing it, which would let two texts share a key. The refusal
    // is an ArgumentException that doesn't quote the text.
    [Fact]
    public void KeyForWrite_ALoneSurrogate_IsRefused()
    {
        using var operation = IdempotentOperation.Begin("operation-1", Secret);

        var refused = Assert.Throws<ArgumentException>(() => IdempotentOperation.KeyForWrite("POST", "https://callee/\ud800", []));
        Assert.Null(refused.InnerException);
        Assert.Throws<ArgumentException>(() => IdempotentOperation.KeyForWrite("PO\udc00ST", Address, []));
    }

    [Fact]
    public void DeriveId_ALoneSurrogate_IsRefused()
    {
        using var operation = IdempotentOperation.Begin("operation-1", Secret);

        var refused = Assert.Throws<ArgumentException>(() => IdempotentOperation.DeriveId("mail\ud800"));
        Assert.Equal("purpose", refused.ParamName);
        Assert.Null(refused.InnerException);
    }

    [Fact]
    public void ForMessage_ALongName_MatchesTheGoldenVector()
    {
        var envelope = Guid.Parse("0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162");

        Assert.Equal(
            "7501aff06f185bd2ac47a4a687343ce3d91d52c9e5022364b36fd1b7ed6fddee:0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162",
            IdempotentOperation.ForMessage(new string('h', 300), envelope));
    }

    // A name whose key would be over the operation key's maximum, though still a valid idempotency key, is shortened
    // too, and one just short of it is kept as it is.
    [Fact]
    public void ForMessage_ANameJustOverTheMaximum_MatchesTheGoldenVector()
    {
        var envelope = Guid.Parse("0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162");

        Assert.Equal(
            "168eec374dd458d9643109ccbb3b7b83f0d5badf98745a5b84ec2a0a432e12a4:0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162",
            IdempotentOperation.ForMessage(new string('h', 164), envelope));
        Assert.Equal($"{new string('h', 163)}:{envelope}", IdempotentOperation.ForMessage(new string('h', 163), envelope));
    }

    [Fact]
    public void ForMessage_ANameThatIsNotVisibleAscii_IsShortenedToItsUtf8Hash()
    {
        var envelope = Guid.Parse("0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162");

        Assert.Equal(
            "6245da155660b3cc7381fd353fa4dbda49838684fc300f11032acd7912cadbd9:0b8a3c1e-5d2f-4e6a-9b7c-1d2e3f405162",
            IdempotentOperation.ForMessage("H\u00e4ndler", envelope));
    }
}
