using System;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Proxy
{
    internal sealed record BitwardenProxyGeneratorOptions(BuildMode Mode)
    {
        public BitwardenProxyGeneratorOptions(AnalyzerConfigOptions options)
            : this(BuildMode.ReverseProxy)
        {

        }

        public string Generate(CancellationToken cancellationToken)
        {
            // Generate the proper entrypoint for the mode
            if (Mode == BuildMode.ReverseProxy)
            {
                return $$"""
                public class Program
                {
                    public static void Main(string[] args)
                    {
                        var builder = WebApplication.CreateBuilder(args);

                        // TODO: Do some static configuration
                        builder.Services.AddReverseProxy()
                            .LoadFromConfig();

                        var app = builder.Build();

                        app.MapReverseProxy();

                        app.Run();
                    }
                }
                """;
            }
            else if (Mode == BuildMode.InProcess)
            {
                return $$"""
                public class Program
                {
                    public static void Main(string args[])
                    {
                        var builder = WebApplication.CreateBuilder(args);

                        // TODO: Add all the services

                        var app = builder.Build();

                        // TODO: Map all the groups

                        app.Run();
                    }
                }
                """;
            }
            else
            {
                throw new Exception("Invalid BuildMode");
            }
        }
    }

    public enum BuildMode
    {
        ReverseProxy,
        InProcess,
    }

    internal sealed record Service(string Project, string? Style)
    {

    }

    internal sealed record CatchallService(string Project, string? Style)
    {

    }
}

