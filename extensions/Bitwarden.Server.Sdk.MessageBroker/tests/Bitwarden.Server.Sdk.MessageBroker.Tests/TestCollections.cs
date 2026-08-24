namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Serializes all in-memory (no-container) test classes so that their ActivityListeners
/// don't capture activities from sibling tests running in parallel, which would cause
/// the tracing-span assertion tests to see more activities than expected.
/// </summary>
[CollectionDefinition("InMemory")]
public class InMemoryCollection { }
