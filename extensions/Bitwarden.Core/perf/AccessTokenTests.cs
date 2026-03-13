using BenchmarkDotNet.Attributes;

namespace Bitwarden.Core.Microbenchmarks;

[MemoryDiagnoser]
public class AccessTokenTests
{
    [Benchmark]
    public AccessToken Parse()
        => AccessToken.Parse("0.4eaea7be-6a0b-4c0b-861e-b033001532a9.ydNqCpyZ8E7a171FjZn89WhKE1eEQF:2WQh70hSQQZFXm+QteNYsg==");
}
