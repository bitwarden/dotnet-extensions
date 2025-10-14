using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bitwarden.Extensions.Configuration.Tests;

public class DebuggerFactAttribute : FactAttribute
{
    public DebuggerFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!Debugger.IsAttached)
        {
            Skip = "This test can only be ran while a debugger is attached.";
        }
    }
}
