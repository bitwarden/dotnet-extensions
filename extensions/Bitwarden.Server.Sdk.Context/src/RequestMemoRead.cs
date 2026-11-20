namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// One read's answer, and what may be done with it.
/// </summary>
/// <typeparam name="T">The answer's type.</typeparam>
/// <param name="Value">The answer, for the reader; shared with others only as <paramref name="Keep"/> allows.</param>
/// <param name="Size">The bytes it holds, counted against the memo's bound when kept.</param>
/// <param name="Keep">Whether it is kept, shared only with callers already waiting, or neither.</param>
internal readonly record struct RequestMemoRead<T>(T Value, long Size, RequestMemoKeep Keep);
