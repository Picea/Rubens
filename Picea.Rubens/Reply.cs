// =============================================================================
// Reply Actor — Hewitt's Request-Reply as a One-Shot Decider
// =============================================================================
// In Hewitt's 1973 Actor Model, there is no "ask" pattern — no synchronous
// request-reply primitive built into the model. Instead, request-reply is
// emergent from the three axioms:
//
//     1. The requester creates a new actor (axiom #2) — the "reply actor"
//     2. The requester includes the reply actor's address in its message (axiom #1)
//     3. The handler sends its result to the reply actor's address (axiom #1)
//     4. The reply actor receives the result and designates terminal behavior (axiom #3)
//
// This is not an optimization or a shortcut — it is the ONLY mechanism for
// request-reply in the pure actor model. The reply actor is a real actor with
// a real mailbox, a real ProcessLoop, and real behavior designation.
//
// The ReplyActor<T> is a Decider on the Picea kernel, proving that the
// single coalgebraic abstraction (Mealy machine → Decider → Actor) handles
// even the most fundamental communication pattern.
//
// State machine:
//     ┌──────────┐   Tell(value)   ┌──────────────┐
//     │ Awaiting  │───────────────▶│  Received(T)  │ ← terminal
//     └──────────┘                 └──────────────┘
//
// The observer captures the received value into a TaskCompletionSource<T>,
// bridging the actor world (asynchronous message-passing) to the caller's
// world (Task-based async/await). This is the boundary — exactly where
// Hewitt's actors meet the request/response protocol of HTTP or RPC.
//
// References:
//   Hewitt (2010). "Actor Model of Computation." arXiv:1008.1459.
//   §3.2: "Every message includes the address of the reply destination."
// =============================================================================

using System.Diagnostics;

using Picea;

namespace Picea.Rubens;

// =============================================================================
// Reply Actor State
// =============================================================================

/// <summary>
/// State of the one-shot reply actor.
/// </summary>
/// <remarks>
/// Two states: <see cref="Awaiting"/> (waiting for a reply) and
/// <see cref="ReplyState{T}.Received"/> (reply received — terminal).
/// </remarks>
public interface ReplyState<T>
{
    /// <summary>
    /// The reply actor is waiting for a message.
    /// </summary>
    record struct Awaiting : ReplyState<T>;

    /// <summary>
    /// The reply actor has received its message and is terminal.
    /// </summary>
    record struct Received(T Value) : ReplyState<T>;
}

// =============================================================================
// Reply Actor Events
// =============================================================================

/// <summary>
/// Events produced by the reply actor.
/// </summary>
public interface ReplyEvent<T>
{
    /// <summary>
    /// A reply value was received.
    /// </summary>
    record struct Replied(T Value) : ReplyEvent<T>;
}

// =============================================================================
// Reply Actor Effects
// =============================================================================

/// <summary>
/// Effects produced by the reply actor's transitions.
/// </summary>
public interface ReplyEffect
{
    /// <summary>
    /// No effect.
    /// </summary>
    record struct None : ReplyEffect;
}

// =============================================================================
// Reply Actor Errors
// =============================================================================

/// <summary>
/// Errors produced when commands to the reply actor are rejected.
/// </summary>
public interface ReplyError
{
    /// <summary>
    /// The reply actor has already received a value — it is terminal.
    /// </summary>
    record struct AlreadyReplied : ReplyError;
}

// =============================================================================
// Reply Actor Decider
// =============================================================================

/// <summary>
/// A one-shot Decider that receives exactly one message, then terminates.
/// </summary>
/// <remarks>
/// <para>
/// This is Hewitt's reply actor: spawned per-request, included as a reply
/// address in the outgoing message, receives the result, and terminates.
/// It uses the exact same <see cref="Actor.Spawn{TDecider,TState,TCommand,TEvent,TEffect,TError,TParameters}"/>
/// mechanism as any other actor — same mailbox, same ProcessLoop, same
/// terminal state detection, same OTel tracing.
/// </para>
/// <para>
/// The <typeparamref name="T"/> parameter is the type of the reply value.
/// For aggregate actors, this is typically <c>Result&lt;TState, TError&gt;</c>.
/// </para>
/// <para>
/// <b>Parameters:</b> <see cref="Unit"/> — the reply actor needs no initialization parameters.
/// The <see cref="TaskCompletionSource{T}"/> that bridges to the caller is captured
/// in the observer closure at spawn time, keeping the Decider pure.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the reply value.</typeparam>
public class ReplyActor<T>
    : Decider<ReplyState<T>, T, ReplyEvent<T>, ReplyEffect, ReplyError, Unit>
{
    /// <summary>
    /// Initial state: awaiting a reply. No startup effects.
    /// </summary>
    public static (ReplyState<T> State, ReplyEffect Effect) Initialize(Unit _) =>
        (new ReplyState<T>.Awaiting(), new ReplyEffect.None());

    /// <summary>
    /// Validates the incoming reply: accepts when awaiting, rejects when already received.
    /// </summary>
    /// <remarks>
    /// A reply actor accepts exactly one message. Any subsequent message is rejected
    /// with <see cref="ReplyError.AlreadyReplied"/>. In practice, the actor terminates
    /// after the first message (via <see cref="IsTerminal"/>), so the reject path is
    /// a defensive guard — the ProcessLoop should already have exited.
    /// </remarks>
    public static Result<ReplyEvent<T>[], ReplyError> Decide(
        ReplyState<T> state,
        T command) =>
        state switch
        {
            ReplyState<T>.Awaiting =>
                Result<ReplyEvent<T>[], ReplyError>
                    .Ok([new ReplyEvent<T>.Replied(command)]),

            ReplyState<T>.Received =>
                Result<ReplyEvent<T>[], ReplyError>
                    .Err(new ReplyError.AlreadyReplied()),

            _ => throw new UnreachableException()
        };

    /// <summary>
    /// Transitions from Awaiting to Received — designates terminal behavior.
    /// </summary>
    public static (ReplyState<T> State, ReplyEffect Effect) Transition(
        ReplyState<T> state,
        ReplyEvent<T> @event) =>
        @event switch
        {
            ReplyEvent<T>.Replied(var value) =>
                (new ReplyState<T>.Received(value), new ReplyEffect.None()),

            _ => throw new UnreachableException()
        };

    /// <summary>
    /// Terminal when a reply has been received — the actor stops processing.
    /// </summary>
    public static bool IsTerminal(ReplyState<T> state) =>
        state is ReplyState<T>.Received;
}

// =============================================================================
// Reply Channel — Spawn + Await Helper
// =============================================================================

/// <summary>
/// Convenience factory for spawning a reply actor and awaiting its result.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open{T}"/> spawns a <see cref="ReplyActor{T}"/> via the standard
/// <see cref="Actor.Spawn{TDecider,TState,TCommand,TEvent,TEffect,TError,TParameters}"/>
/// path and returns both the reply address (for the sender to <see cref="Address{T}.Tell"/>
/// the result to) and a <see cref="Task{T}"/> that completes when the reply arrives.
/// </para>
/// <para>
/// The <see cref="TaskCompletionSource{T}"/> is captured in the observer closure,
/// keeping the <see cref="ReplyActor{T}"/> Decider pure. The observer's side-effect
/// is to set the TCS result when the Replied event is observed — this is the
/// same pattern as a persistence observer writing to a database.
/// </para>
/// <example>
/// <code>
/// // Spawn a reply actor — pure Hewitt request-reply
/// var (replyAddress, replyTask) = await ReplyChannel.Open&lt;Result&lt;UserState, UserError&gt;&gt;(ct);
///
/// // Send command with reply address to aggregate actor
/// await aggregateAddress.Tell(new Envelope&lt;UserCommand, Result&lt;UserState, UserError&gt;&gt;(command, replyAddress));
///
/// // Await the reply — the reply actor will receive it and set the TCS
/// var result = await replyTask;
/// </code>
/// </example>
/// </remarks>
public static class ReplyChannel
{
    /// <summary>
    /// Spawns a one-shot reply actor and returns its address and a task that completes with the reply.
    /// </summary>
    /// <typeparam name="T">The type of the reply value.</typeparam>
    /// <param name="cancellationToken">Token to cancel the reply wait.</param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item><see cref="Address{T}"/> — the reply address to include in outgoing messages</item>
    ///   <item><see cref="Task{T}"/> — completes when the reply actor receives a message</item>
    /// </list>
    /// </returns>
    public static async ValueTask<(Address<T> Address, Task<T> Task)> Open<T>(
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register cancellation to propagate to the TCS
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        // Observer: when the reply event is observed, complete the TCS.
        // This bridges the actor world to the caller's async/await world.
        Observer<ReplyState<T>, ReplyEvent<T>, ReplyEffect> observer = (state, @event, _) =>
        {
            if (@event is ReplyEvent<T>.Replied(var value))
                tcs.TrySetResult(value);

            return PipelineResult.Ok;
        };

        // No-op interpreter: reply actors produce no effects
        Interpreter<ReplyEffect, ReplyEvent<T>> interpreter = _ =>
            new ValueTask<Result<ReplyEvent<T>[], PipelineError>>(
                Result<ReplyEvent<T>[], PipelineError>.Ok([]));

        // Spawn via the standard Actor.Spawn — same mailbox, same ProcessLoop,
        // same OTel tracing, same terminal state detection. One model.
        var address = await Actor.Spawn<ReplyActor<T>, ReplyState<T>, T,
            ReplyEvent<T>, ReplyEffect, ReplyError, Unit>(
            default,
            observer,
            interpreter,
            mailboxCapacity: 1, // One-shot: capacity 1 is sufficient
            cancellationToken).ConfigureAwait(false);

        return (address, tcs.Task);
    }
}
