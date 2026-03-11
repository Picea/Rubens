// =============================================================================
// Counter Decider — Shared Test Domain Logic
// =============================================================================
// The SAME transition function used by all three runtimes (MVU, ES, Actor).
// This proves the Picea kernel is truly runtime-agnostic.
// =============================================================================

using System.Diagnostics;

using Picea;

namespace Picea.Rubens.Tests;

/// <summary>
/// The state of the counter.
/// </summary>
public readonly record struct CounterState(int Count);

/// <summary>
/// Commands representing user intent for the counter.
/// </summary>
public interface CounterCommand
{
    /// <summary>Add an amount to the counter (can be negative for subtraction).</summary>
    record struct Add(int Amount) : CounterCommand;

    /// <summary>Reset the counter to zero.</summary>
    record struct Reset : CounterCommand;
}

/// <summary>
/// Events that can occur in the counter domain.
/// </summary>
public interface CounterEvent
{
    record struct Increment : CounterEvent;
    record struct Decrement : CounterEvent;
    record struct Reset : CounterEvent;
}

/// <summary>
/// Errors produced when command validation fails.
/// </summary>
public interface CounterError
{
    /// <summary>The resulting count would exceed the upper bound.</summary>
    record struct Overflow(int Current, int Amount, int Max) : CounterError;

    /// <summary>The resulting count would go below zero.</summary>
    record struct Underflow(int Current, int Amount) : CounterError;

    /// <summary>Reset requested when counter is already at zero.</summary>
    record struct AlreadyAtZero : CounterError;
}

/// <summary>
/// Effects produced by counter transitions.
/// </summary>
public interface CounterEffect
{
    record struct None : CounterEffect;
    record struct Log(string Message) : CounterEffect;
}

/// <summary>
/// The counter Decider — pure domain logic, no runtime dependency.
/// </summary>
public class Counter
    : Decider<CounterState, CounterCommand, CounterEvent, CounterEffect, CounterError, Unit>
{
    /// <summary>
    /// Upper bound for the counter value.
    /// </summary>
    public const int MaxCount = 100;

    /// <summary>
    /// Initial state: count is zero, no startup effects.
    /// </summary>
    public static (CounterState State, CounterEffect Effect) Initialize(Unit _) =>
        (new CounterState(0), new CounterEffect.None());

    /// <summary>
    /// Validates a command against the current state, producing events or an error.
    /// </summary>
    public static Result<CounterEvent[], CounterError> Decide(
        CounterState state,
        CounterCommand command) =>
        command switch
        {
            CounterCommand.Add(var amount) when state.Count + amount > MaxCount =>
                Result<CounterEvent[], CounterError>
                    .Err(new CounterError.Overflow(state.Count, amount, MaxCount)),

            CounterCommand.Add(var amount) when state.Count + amount < 0 =>
                Result<CounterEvent[], CounterError>
                    .Err(new CounterError.Underflow(state.Count, amount)),

            CounterCommand.Add(var amount) when amount >= 0 =>
                Result<CounterEvent[], CounterError>
                    .Ok(Enumerable.Repeat<CounterEvent>(new CounterEvent.Increment(), amount).ToArray()),

            CounterCommand.Add(var amount) =>
                Result<CounterEvent[], CounterError>
                    .Ok(Enumerable.Repeat<CounterEvent>(new CounterEvent.Decrement(), Math.Abs(amount)).ToArray()),

            CounterCommand.Reset when state.Count is 0 =>
                Result<CounterEvent[], CounterError>
                    .Err(new CounterError.AlreadyAtZero()),

            CounterCommand.Reset =>
                Result<CounterEvent[], CounterError>
                    .Ok([new CounterEvent.Reset()]),

            _ => throw new UnreachableException()
        };

    /// <summary>
    /// Pure transition: given state and event, produce new state and effect.
    /// </summary>
    public static (CounterState State, CounterEffect Effect) Transition(
        CounterState state,
        CounterEvent @event) =>
        @event switch
        {
            CounterEvent.Increment =>
                (state with { Count = state.Count + 1 }, new CounterEffect.None()),

            CounterEvent.Decrement =>
                (state with { Count = state.Count - 1 }, new CounterEffect.None()),

            CounterEvent.Reset =>
                (new CounterState(0), new CounterEffect.Log($"Counter reset from {state.Count}")),

            _ => throw new UnreachableException()
        };
}
