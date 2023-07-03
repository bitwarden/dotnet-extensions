using System.Diagnostics;

namespace Bitwarden.Extensions.Configuration.Tests;

public class DebuggerFactAttribute : FactAttribute
{
    public DebuggerFactAttribute()
    {
        if (!Debugger.IsAttached)
        {
            Skip = "This test can only be ran while a debugger is attached.";
        }
    }
}
