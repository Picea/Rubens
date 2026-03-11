// =============================================================================
// Envelope — Command + Reply Address
// =============================================================================
// In Hewitt's actor model, every message that expects a reply must include the
// address of the reply destination. This is not an optional feature or a
// convenience pattern — it is the fundamental mechanism for request-reply
// communication in the actor model.
//
//     "Every message includes the address of the reply destination."
//     — Hewitt (2010), §3.2
//
// The Envelope wraps a domain command with the reply actor's address. The
// aggregate actor unwraps the envelope, processes the command, and sends the
// result to the reply address. This is Hewitt's axiom #1 in action:
// "send a finite number of messages to other actors."
//
// The envelope is typed on both the command type and the reply type, providing
// compile-time safety: you cannot send a command envelope to an actor that
// doesn't know how to handle it, and you cannot attach a reply address of
// the wrong type.
// =============================================================================

namespace Picea.Rubens;

/// <summary>
/// A command envelope carrying a reply address — Hewitt's request-reply primitive.
/// </summary>
/// <remarks>
/// <para>
/// The envelope pairs a domain command with the address of a reply actor.
/// The receiving actor processes the command and sends its result to the
/// reply address via <see cref="Address{TCommand}.Tell"/>.
/// </para>
/// <para>
/// This is how request-reply works in the pure actor model: no synchronous
/// calls, no Ask pattern, no future/promise built into the mailbox. Just
/// actors sending messages to other actors, with the reply destination
/// explicitly included in the message.
/// </para>
/// <example>
/// <code>
/// // Spawn a reply actor
/// var (replyAddress, replyTask) = await ReplyChannel.Open&lt;Result&lt;UserState, UserError&gt;&gt;(ct);
///
/// // Wrap command in envelope with reply address
/// var envelope = new Envelope&lt;UserCommand, Result&lt;UserState, UserError&gt;&gt;(
///     new UserCommand.Register(...),
///     replyAddress);
///
/// // Send to aggregate actor
/// await aggregateAddress.Tell(envelope);
///
/// // Await reply from the reply actor
/// var result = await replyTask;
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="TCommand">The domain command type.</typeparam>
/// <typeparam name="TReply">The reply value type (typically <c>Result&lt;TState, TError&gt;</c>).</typeparam>
/// <param name="Command">The domain command to process.</param>
/// <param name="ReplyTo">The reply actor's address — where to send the result.</param>
public record Envelope<TCommand, TReply>(
    TCommand Command,
    Address<TReply> ReplyTo);
