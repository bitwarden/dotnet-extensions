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

    // Golden vectors computed outside .NET (Python's hashlib and uuid), so a client in another language can match the
    // keys, and so a change that derives a different key for the same write fails here: a key is a wire format, and a
    // run after an upgrade that derives other keys does its writes twice.
    [Fact]
    public void KeyForWrite_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1");

        Assert.Equal(
            "b9c0297a16c05917f933e9c467632c5ab9174f4335bd4bf16fda9776181b5b3e",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "0faaa2bea2237fba0422bce90e2d3694d5de2b9ad3442a448a76d65d0e5e9acc",
            IdempotentOperation.KeyForWrite("POST", Address, "{\"a\":1}"u8));
        Assert.Equal(
            "e8676ed8fc9b8d2cce9d9da6d1cbd066e9031e7701887908df9463e76a34cbf3",
            IdempotentOperation.KeyForWrite("DELETE", Address, []));
    }

    [Fact]
    public void DeriveId_MatchesTheGoldenVectors()
    {
        using var operation = IdempotentOperation.Begin("operation-1");

        Assert.Equal(Guid.Parse("1b6ab81d-515e-8cdc-b188-66740a4b2127"), IdempotentOperation.DeriveId("mail"));
        Assert.Equal(Guid.Parse("c808cbf8-f938-8493-a3e9-c4417785220e"), IdempotentOperation.DeriveId("mail"));
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
