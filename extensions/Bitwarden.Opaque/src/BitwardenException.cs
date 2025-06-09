namespace Bitwarden.Opaque;

/// <summary>
///  A class to represent an exception thrown by the Bitwarden OPAQUE library.
/// </summary>
/// <param name="errorCode">A numeric error code, to separate different error types</param>
/// <param name="message">The error message</param>
public class BitwardenException(int errorCode, string message) : Exception($"Error {getCodeName(errorCode)} - {message}")
{
    private static string getCodeName(int code)
    {
        // Important: This needs to be kept in sync with the error codes in the rust library.
        return code switch
        {
            0 => "OK",
            1 => "INVALID_INPUT",
            2 => "INVALID_CONFIG",
            3 => "PROTOCOL_ERROR",
            4 => "INTERNAL_ERRROR",

            // This is a special case and it's only used in the C# code.
            100 => "UNEXPECTED_RETURN",
            _ => "UNKNOWN",
        };
    }
}
