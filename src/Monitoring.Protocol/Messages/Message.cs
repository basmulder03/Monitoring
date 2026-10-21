using System.Text.Json.Serialization;

namespace Monitoring.Protocol.Messages;

/// <summary>
/// Base type of every message on every hop. The JSON <c>type</c> discriminator carries the message name.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Heartbeat), "heartbeat")]
[JsonDerivedType(typeof(Pull), "pull")]
[JsonDerivedType(typeof(PullResult), "pullResult")]
[JsonDerivedType(typeof(Snapshot), "snapshot")]
[JsonDerivedType(typeof(Delta), "delta")]
[JsonDerivedType(typeof(Goodbye), "goodbye")]
[JsonDerivedType(typeof(Config), "config")]
[JsonDerivedType(typeof(Ack), "ack")]
public abstract record Message;
