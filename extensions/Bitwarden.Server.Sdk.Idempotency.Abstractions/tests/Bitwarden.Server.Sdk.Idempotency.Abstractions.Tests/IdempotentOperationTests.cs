using Org.BouncyCastle.Crypto.Digests;

namespace Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests;

public sealed class IdempotentOperationTests
{
    private const string Address = "https://callee/api/v1/things";

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
            using var operation = IdempotentOperation.Begin("operation-1");
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
            using var operation = IdempotentOperation.Begin("operation-1");
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
            using var operation = IdempotentOperation.Begin("operation-1");
            return IdempotentOperation.KeyForWrite("PUT", Address, System.Text.Encoding.UTF8.GetBytes(body));
        }

        Assert.NotEqual(Run("one"), Run("two"));
    }

    [Fact]
    public void AnotherOperation_DerivesOtherKeysAndIds()
    {
        (string? Key, Guid? Id) Run(string key)
        {
            using var operation = IdempotentOperation.Begin(key);
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
        using var operation = IdempotentOperation.Begin("operation-1");

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
        using (IdempotentOperation.Begin("outer"))
        {
            using (IdempotentOperation.Begin("inner"))
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
        var outer = IdempotentOperation.Begin("outer-op");
        var inner = IdempotentOperation.Begin("inner-op");

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
        using var outer = IdempotentOperation.Begin("outer-op");
        var inner = IdempotentOperation.Begin("inner-op");
        inner.Dispose();

        using var next = IdempotentOperation.Begin("next-op");
        inner.Dispose();

        Assert.Equal("next-op", IdempotentOperation.Current);
    }

    [Fact]
    public void Begin_AKeyOfTheMaximumLength_Begins()
    {
        using var operation = IdempotentOperation.Begin(new string('k', IdempotentOperation.MaximumKeyLength));

        Assert.Equal(IdempotentOperation.MaximumKeyLength, IdempotentOperation.Current?.Length);
    }

    // Longer than an operation key may be, though still a valid idempotency key.
    [Theory]
    [InlineData(IdempotentOperation.MaximumKeyLength + 1)]
    [InlineData(IdempotencyKeys.MaximumLength)]
    public void Begin_AKeyLongerThanTheMaximum_IsRefused(int length)
    {
        Assert.Throws<ArgumentException>(() => IdempotentOperation.Begin(new string('k', length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has a space")]
    [InlineData("naïve")]
    public void Begin_AKeyThatIsNotVisibleAscii_IsRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => IdempotentOperation.Begin(key));
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
        using var operation = IdempotentOperation.Begin(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForMessage_ABlankName_IsRefused(string? handler)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotentOperation.ForMessage(handler!, Guid.NewGuid()));
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

    // A field that holds a newline can't pass for two fields: each is its own element of the tuple. Each write is the
    // first of its operation, so only the fields tell the keys apart.
    [Fact]
    public void KeyForWrite_AMethodAndAddressAreNeverAmbiguous()
    {
        string? Run(string method, string address)
        {
            using var operation = IdempotentOperation.Begin("operation-1");
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
            using var operation = IdempotentOperation.Begin("operation-1");
            if (first)
            {
                IdempotentOperation.KeyForWrite("POST", "https://callee/a\nhttps://callee/b", []);
            }

            return IdempotentOperation.KeyForWrite("POST\nhttps://callee/a", "https://callee/b", []);
        }

        Assert.Equal(Run(first: false), Run(first: true));
    }

    // Golden vectors computed outside .NET, so a client in another language can match the keys, and so a change that
    // derives a different key for the same write fails here: a key is a wire format, and a run after an upgrade that
    // derives other keys does its writes twice. Computed with pycryptodome 4.3.0:
    //
    //   from Crypto.Hash import TupleHash256
    //   import uuid
    //
    //   def write_key(op, method, address, body, ordinal):
    //       h = TupleHash256.new(digest_bytes=32, custom=b"Bitwarden idempotency key")
    //       for e in (op.encode(), method.encode(), address.encode(), body, str(ordinal).encode()):
    //           h.update(e)
    //       return h.hexdigest()
    //
    //   def derive_id(op, purpose, ordinal):
    //       h = TupleHash256.new(digest_bytes=32, custom=b"Bitwarden derived identifier")
    //       for e in (op.encode(), purpose.encode(), str(ordinal).encode()):
    //           h.update(e)
    //       b = bytearray(h.digest()[:16])
    //       b[6] = (b[6] & 0x0F) | 0x80
    //       b[8] = (b[8] & 0x3F) | 0x80
    //       return uuid.UUID(bytes=bytes(b))
    //
    //   write_key("operation-1", "POST", "https://callee/api/v1/things", b'{"a":1}', 0)
    [Fact]
    public void KeyForWrite_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1");

        Assert.Equal(
            "06b9a7165ec09e196c56a1a140ad8397438e82bdcfb1f3dfa8c3d97e7fca6e60",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "416dfe7157a7716c317e2754684c9ba179e9a5eedc59e78e383ca4240aeff03f",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "c19a2f7b98f10097ff4fa70ba0d09c3f00a19cb69a5b85fc15c627ad81cda8c2",
            IdempotentOperation.KeyForWrite("DELETE", Address, []));
    }

    // Computed with derive_id above.
    [Fact]
    public void DeriveId_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1");

        Assert.Equal(Guid.Parse("bbc9c510-7bb9-8bb0-af4f-179e05157b9b"), IdempotentOperation.DeriveId("mail"));
        Assert.Equal(Guid.Parse("2d441064-7039-8d5d-8ef1-4128aef4d8ad"), IdempotentOperation.DeriveId("mail"));
    }

    // The keys rely on each BlockUpdate being one element of the tuple. These are the TupleHash samples from NIST's
    // SP 800-185 example values: #1 to #3 for TupleHash128 and #4 to #6 for TupleHash256. The output length is part of
    // TupleHash's input, so each asks for the length its sample gives.
    [Theory]
    [InlineData(128, "", 2, "c5d8786c1afb9b82111ab34b65b2c0048fa64e6d48e263264ce1707d3ffc8ed1")]
    [InlineData(128, "My Tuple App", 2, "75cdb20ff4db1154e841d758e24160c54bae86eb8c13e7f5f40eb35588e96dfb")]
    [InlineData(128, "My Tuple App", 3, "e60f202c89a2631eda8d4c588ca5fd07f39e5151998deccf973adb3804bb6e84")]
    [InlineData(256, "", 2, "cfb7058caca5e668f81a12a20a2195ce97a925f1dba3e7449a56f82201ec607311ac2696b1ab5ea2352df1423bde7bd4bb78c9aed1a853c78672f9eb23bbe194")]
    [InlineData(256, "My Tuple App", 2, "147c2191d5ed7efd98dbd96d7ab5a11692576f5fe2a5065f3e33de6bba9f3aa1c4e9a068a289c61c95aab30aee1e410b0b607de3620e24a4e3bf9852a1d4367e")]
    [InlineData(256, "My Tuple App", 3, "45000be63f9b6bfd89f54717670f69a9bc763591a4f05c50d68891a744bcc6e7d6d5b5e82c018da999ed35b0bb49c9678e526abd8e85c13ed254021db9e790ce")]
    public void TupleHash_TreatsEachBlockUpdateAsOneElement(int strength, string customization, int elements, string expected)
    {
        byte[][] tuple = [Convert.FromHexString("000102"), Convert.FromHexString("101112131415"), Convert.FromHexString("202122232425262728")];
        var digest = new TupleHash(strength, System.Text.Encoding.UTF8.GetBytes(customization), strength * 2);
        foreach (var element in tuple.Take(elements))
        {
            digest.BlockUpdate(element);
        }

        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);

        Assert.Equal(expected, Convert.ToHexStringLower(output));
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
