// =============================================================================
// Test Helpers for Actor Testing
// =============================================================================
// Production actors are opaque — you can only send commands via Address<TCommand>.
// Test helpers provide controlled visibility into internal state and events for
// verification purposes.
//
// These helpers are in the test project, NOT the framework — Hewitt's model
// requires actor state to be encapsulated. Test visibility is a testing concern.
// =============================================================================

using System.Threading.Channels;

using Picea;

namespace Picea.Rubens.Testing;

/// <summary>
/// A test wrapper around a production actor that captures state transitions and events.
/// </summary>
public sealed class TestActor<TDecider, TState, TCommand, TEvent, TEffect, TError, TParameters>
    where TDecider : Decider<TState, TCommand, TEvent, TEffect, TError, TParameters>
{
    private readonly Channel<TCommand> _channel;
    private int _processedCount;

    /// <summary>
    /// The address to send commands to this actor.
    /// </summary>
    public Address<TCommand> Address { get; }

    /// <summary>
    /// The current state of the actor (test-only visibility).
    /// </summary>
    public TState State { get; private set; }

    /// <summary>
    /// All events produced by the actor's decisions (test-only visibility).
    /// </summary>
    public List<TEvent> Events { get; } = [];

    /// <summary>
    /// All effects produced by the actor's transitions (test-only visibility).
    /// </summary>
    public List<TEffect> Effects { get; } = [];

    /// <summary>
    /// All errors from rejected commands (test-only visibility).
    /// </summary>
    public List<TError> Errors { get; } = [];

    private TestActor(Channel<TCommand> channel, Address<TCommand> address, TState initialState)
    {
        _channel = channel;
        Address = address;
        State = initialState;
    }

    /// <summary>
    /// Spawns a test actor with full observability.
    /// </summary>
    /// <param name="parameters">Initialization parameters for the Decider.</param>
    /// <param name="interpreter">Optional interpreter for effect handling. Defaults to no-op.</param>
    /// <param name="mailboxCapacity">Bounded mailbox size.</param>
    /// <param name="cancellationToken">Token to stop the actor.</param>
    public static async ValueTask<TestActor<TDecider, TState, TCommand, TEvent, TEffect, TError, TParameters>> Spawn(
        TParameters parameters,
        Interpreter<TEffect, TEvent>? interpreter = null,
        int mailboxCapacity = Actor.DefaultMailboxCapacity,
        CancellationToken cancellationToken = default)
    {
        var (initialState, _) = TDecider.Initialize(parameters);

        var channel = Channel.CreateBounded<TCommand>(
            new BoundedChannelOptions(mailboxCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        var address = new Address<TCommand>(channel.Writer);
        var testActor = new TestActor<TDecider, TState, TCommand, TEvent, TEffect, TError, TParameters>(
            channel, address, initialState);

        // Capturing observer: records state, events, and effects
        Observer<TState, TEvent, TEffect> observer = (state, @event, effect) =>
        {
            testActor.State = state;
            testActor.Events.Add(@event);
            testActor.Effects.Add(effect);
            return PipelineResult.Ok;
        };

        var effectInterpreter = interpreter
            ?? (_ => new ValueTask<Result<TEvent[], PipelineError>>(
                Result<TEvent[], PipelineError>.Ok([])));

        var runtime = await DecidingRuntime<TDecider, TState, TCommand, TEvent, TEffect, TError, TParameters>
            .Start(parameters, observer, effectInterpreter, threadSafe: false, trackEvents: false, cancellationToken)
            .ConfigureAwait(false);

        _ = testActor.ProcessLoop(runtime, channel.Reader, cancellationToken);

        return testActor;
    }

    /// <summary>
    /// Waits until all sent commands have been processed.
    /// </summary>
    public async Task Drain()
    {
        var sent = Volatile.Read(ref Address._sentCount);
        while (Volatile.Read(ref _processedCount) < sent)
            await Task.Yield();
    }

    /// <summary>
    /// Completes the mailbox, preventing further commands.
    /// </summary>
    public void Stop() =>
        _channel.Writer.TryComplete();

    private async Task ProcessLoop(
        DecidingRuntime<TDecider, TState, TCommand, TEvent, TEffect, TError, TParameters> runtime,
        ChannelReader<TCommand> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await runtime.Handle(command, cancellationToken).ConfigureAwait(false);

                result.Switch(
                    _ => { },
                    error => Errors.Add(error));

                Interlocked.Increment(ref _processedCount);

                if (runtime.IsTerminal)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative shutdown
        }
        finally
        {
            runtime.Dispose();
        }
    }
}
