using Microsoft.CodeAnalysis;

namespace Bitwarden.Proxy
{
    [Generator]
    public class BitwardenProxyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var proxyOptions = context.AnalyzerConfigOptionsProvider
                .Select(static (options, ct) => new BitwardenProxyGeneratorOptions(options.GlobalOptions));


        }
    }
}
