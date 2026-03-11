// =============================================================================
// Address — Hewitt's Unforgeable Capability
// =============================================================================
// In Hewitt's 1973 Actor Model, an address is the sole mechanism for
// communicating with an actor. It is:
//
//   - Unforgeable: you cannot synthesize an address; you can only obtain one
//     by (a) creating an actor, (b) receiving it in a message, or (c) already
//     having it. This is the "locality" property.
//
//   - Opaque: you cannot inspect an address to learn anything about the actor
//     behind it (no hierarchy, no name, no type information).
//
//   - A capability: possessing an address IS the authorization to send.
//     There is no separate access-control layer.
//
// The Address is typed on TCommand because all actors in this system are
// Deciders — they accept commands, not raw events. The Decider validates
// the command and produces events internally.
//
// Reference:
//   Hewitt, Bishop, Steiger (1973). "A Universal Modular Actor Formalism
//   for Artificial Intelligence." IJCAI'73, pp. 235–245.
// =============================================================================

using System.Threading.Channels;

namespace Picea.Rubens;

/// <summary>
/// An opaque, unforgeable capability to send commands to an actor.
/// </summary>
/// <remarks>
/// <para>
/// This is Hewitt's "mailing address" — the only way to communicate with an actor.
/// You cannot create an <see cref="Address{TCommand}"/> directly; you obtain one by
/// spawning an actor via <see cref="Actor.Spawn{TDecider,TState,TCommand,TEvent,TEffect,TError,TParameters}"/>.
/// </para>
/// <para>
/// The address is typed on <typeparamref name="TCommand"/> because all actors in this
/// system are Deciders: they accept commands (intent), validate them against state,
/// and produce events (facts). The typing ensures compile-time safety — you cannot
/// send a thermostat command to a counter actor.
/// </para>
/// <para>
/// <b>Hewitt's three axioms</b> map to this type as follows:
/// <list type="number">
///   <item><b>Send messages:</b> <see cref="Tell"/> — fire-and-forget, asynchronous</item>
///   <item><b>Create actors:</b> <see cref="Actor.Spawn{TDecider,TState,TCommand,TEvent,TEffect,TError,TParameters}"/> returns a new Address</item>
///   <item><b>Designate behavior:</b> The Decider's Transition function (already in the kernel)</item>
/// </list>
/// </para>
/// <example>
/// <code>
/// // Spawn creates an actor and returns its address
/// var address = Actor.Spawn&lt;Counter, CounterState, CounterCommand,
///     CounterEvent, CounterEffect, CounterError, Unit&gt;();
///
/// // Tell sends a command (fire-and-forget, Hewitt axiom #1)
/// await address.Tell(new CounterCommand.Add(1));
///
/// // Addresses can be passed in messages (variable topology)
/// await otherActor.Tell(new ForwardTo(address));
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="TCommand">The command type this actor accepts.</typeparam>
public sealed class Address<TCommand>
{
    private readonly ChannelWriter<TCommand> _writer;
    internal int _sentCount;

    internal Address(ChannelWriter<TCommand> writer) =>
        _writer = writer;

    /// <summary>
    /// Sends a command to the actor's mailbox asynchronously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is Hewitt's axiom #1: "send a finite number of messages to other actors."
    /// The send is asynchronous and fire-and-forget — the caller receives no response.
    /// </para>
    /// <para>
    /// If the mailbox is bounded and full, the call awaits until space is available
    /// (backpressure). If the actor has been stopped, the call throws
    /// <see cref="ChannelClosedException"/>.
    /// </para>
    /// </remarks>
    /// <param name="command">The command to send.</param>
    public async ValueTask Tell(TCommand command)
    {
        Interlocked.Increment(ref _sentCount);
        await _writer.WriteAsync(command);
    }
}
