# Bitwarden.Server.Sdk.MessageBroker.Sender

**Audience:** engineers writing a Bitwarden service that sends events or commands with
`Bitwarden.Server.Sdk.MessageBroker` and keeps its data in a database through Entity Framework Core.

## About

A transactional outbox for `Bitwarden.Server.Sdk.MessageBroker`: a message is sent if, and only if,
the change it belongs to commits, and a broker that is down can't lose it. It depends on
`Bitwarden.Server.Sdk.MessageBroker` and Entity Framework Core's relational provider.
