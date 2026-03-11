// =============================================================================
// Actor Tests — Hewitt's Three Axioms on the Picea Kernel
// =============================================================================
// These tests prove the actor primitives correctly implement Hewitt's 1973 model:
//
//     Axiom 1: Send messages to other actors         → Tell tests
//     Axiom 2: Create new actors                     → Spawn tests
//     Axiom 3: Designate behavior for next message   → Transition tests
// =============================================================================

using Picea;
using Picea.Rubens;
using Picea.Rubens.Testing;

namespace Picea.Rubens.Tests;

public class ActorTests
{
    // =========================================================================
    // Axiom 2: Create new actors
    // =========================================================================

    [Fact]
    public async Task Spawn_creates_actor_with_initial_state()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        Assert.Equal(0, actor.State.Count);

        actor.Stop();
    }

    [Fact]
    public async Task Spawn_returns_address_capability()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        Assert.NotNull(actor.Address);

        actor.Stop();
    }

    [Fact]
    public async Task Spawn_creates_distinct_actors()
    {
        var actor1 = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);
        var actor2 = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor1.Address.Tell(new CounterCommand.Add(5));
        await actor1.Drain();

        Assert.Equal(5, actor1.State.Count);
        Assert.Equal(0, actor2.State.Count);

        actor1.Stop();
        actor2.Stop();
    }

    // =========================================================================
    // Axiom 1: Send messages to other actors
    // =========================================================================

    [Fact]
    public async Task Tell_sends_command_to_actor()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Add(3));
        await actor.Drain();

        Assert.Equal(3, actor.State.Count);

        actor.Stop();
    }

    [Fact]
    public async Task Tell_is_fire_and_forget()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Add(1));
        await actor.Address.Tell(new CounterCommand.Add(2));
        await actor.Address.Tell(new CounterCommand.Add(3));

        await actor.Drain();

        Assert.Equal(6, actor.State.Count);

        actor.Stop();
    }

    [Fact]
    public async Task Tell_applies_backpressure_when_mailbox_full()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(
                default, mailboxCapacity: 2);

        for (var i = 0; i < 10; i++)
            await actor.Address.Tell(new CounterCommand.Add(1));

        await actor.Drain();

        Assert.Equal(10, actor.State.Count);

        actor.Stop();
    }

    // =========================================================================
    // Axiom 3: Designate behavior for next message
    // =========================================================================

    [Fact]
    public async Task Transition_designates_next_behavior()
    {
        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(default);

        await actor.Address.Tell(new ThermostatCommand.SetTarget(25m));
        await actor.Drain();

        Assert.Equal(25m, actor.State.TargetTemp);

        await actor.Address.Tell(new ThermostatCommand.RecordReading(18m));
        await actor.Drain();

        Assert.Equal(18m, actor.State.CurrentTemp);
        Assert.True(actor.State.Heating);

        actor.Stop();
    }

    [Fact]
    public async Task Decide_rejects_invalid_commands()
    {
        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(default);

        await actor.Address.Tell(new ThermostatCommand.SetTarget(50m));
        await actor.Drain();

        Assert.Single(actor.Errors);
        Assert.IsType<ThermostatError.InvalidTarget>(actor.Errors[0]);
        Assert.Equal(22m, actor.State.TargetTemp);

        actor.Stop();
    }

    [Fact]
    public async Task Decide_produces_events_on_valid_commands()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Add(5));
        await actor.Drain();

        Assert.Equal(5, actor.Events.Count);
        Assert.All(actor.Events, e => Assert.IsType<CounterEvent.Increment>(e));

        actor.Stop();
    }

    // =========================================================================
    // Sequential processing
    // =========================================================================

    [Fact]
    public async Task Actor_processes_commands_sequentially()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => actor.Address.Tell(new CounterCommand.Add(1)).AsTask());
        await Task.WhenAll(tasks);

        await actor.Drain();

        Assert.Equal(100, actor.State.Count);

        actor.Stop();
    }

    // =========================================================================
    // Terminal behavior
    // =========================================================================

    [Fact]
    public async Task Actor_stops_on_terminal_state()
    {
        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(default);

        await actor.Address.Tell(new ThermostatCommand.Shutdown());
        await actor.Drain();

        Assert.False(actor.State.Active);

        actor.Stop();
    }

    // =========================================================================
    // Effect handling
    // =========================================================================

    [Fact]
    public async Task Actor_handles_effects_via_interpreter()
    {
        var notifications = new List<string>();

        Interpreter<ThermostatEffect, ThermostatEvent> interpreter = effect =>
        {
            if (effect is ThermostatEffect.SendNotification notification)
                notifications.Add(notification.Message);

            return new ValueTask<Result<ThermostatEvent[], PipelineError>>(
                Result<ThermostatEvent[], PipelineError>.Ok([]));
        };

        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(
                default, interpreter: interpreter);

        await actor.Address.Tell(new ThermostatCommand.Shutdown());
        await actor.Drain();

        Assert.Single(notifications);
        Assert.Equal("Thermostat shut down", notifications[0]);

        actor.Stop();
    }

    [Fact]
    public async Task Actor_produces_effects_on_transitions()
    {
        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(default);

        await actor.Address.Tell(new ThermostatCommand.RecordReading(18m));
        await actor.Drain();

        Assert.Contains(actor.Effects, e => e is ThermostatEffect.ActivateHeater);

        actor.Stop();
    }

    // =========================================================================
    // Production Spawn (no test observability)
    // =========================================================================

    [Fact]
    public async Task Production_spawn_returns_opaque_address()
    {
        using var cts = new CancellationTokenSource();

        Observer<CounterState, CounterEvent, CounterEffect> observer =
            (_, _, _) => PipelineResult.Ok;

        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.Spawn<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        Assert.NotNull(address);

        await address.Tell(new CounterCommand.Add(1));

        await Task.Delay(50);

        await cts.CancelAsync();
    }

    // =========================================================================
    // Heater cycle (domain integration)
    // =========================================================================

    [Fact]
    public async Task Heater_cycle_produces_correct_state()
    {
        var actor = await TestActor<Thermostat, ThermostatState, ThermostatCommand,
            ThermostatEvent, ThermostatEffect, ThermostatError, Unit>.Spawn(default);

        await actor.Address.Tell(new ThermostatCommand.RecordReading(18m));
        await actor.Drain();

        Assert.Equal(18m, actor.State.CurrentTemp);
        Assert.True(actor.State.Heating);

        await actor.Address.Tell(new ThermostatCommand.RecordReading(23m));
        await actor.Drain();

        Assert.Equal(23m, actor.State.CurrentTemp);
        Assert.False(actor.State.Heating);

        actor.Stop();
    }

    // =========================================================================
    // Counter domain — decrement and errors
    // =========================================================================

    [Fact]
    public async Task Counter_rejects_overflow()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Add(101));
        await actor.Drain();

        Assert.Single(actor.Errors);
        Assert.IsType<CounterError.Overflow>(actor.Errors[0]);
        Assert.Equal(0, actor.State.Count);

        actor.Stop();
    }

    [Fact]
    public async Task Counter_rejects_underflow()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Add(-1));
        await actor.Drain();

        Assert.Single(actor.Errors);
        Assert.IsType<CounterError.Underflow>(actor.Errors[0]);
        Assert.Equal(0, actor.State.Count);

        actor.Stop();
    }

    [Fact]
    public async Task Counter_reset_when_already_zero_is_rejected()
    {
        var actor = await TestActor<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>.Spawn(default);

        await actor.Address.Tell(new CounterCommand.Reset());
        await actor.Drain();

        Assert.Single(actor.Errors);
        Assert.IsType<CounterError.AlreadyAtZero>(actor.Errors[0]);

        actor.Stop();
    }

    // =========================================================================
    // Reply Actor — Hewitt's request-reply as a one-shot Decider
    // =========================================================================

    [Fact]
    public async Task ReplyChannel_Open_spawns_reply_actor_and_returns_address()
    {
        var (replyAddress, replyTask) = await ReplyChannel.Open<int>();

        Assert.NotNull(replyAddress);
        Assert.NotNull(replyTask);
        Assert.False(replyTask.IsCompleted);

        await replyAddress.Tell(42);

        var result = await replyTask;
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ReplyChannel_receives_single_value_and_terminates()
    {
        var (replyAddress, replyTask) = await ReplyChannel.Open<string>();

        await replyAddress.Tell("hello from actor");

        var result = await replyTask;
        Assert.Equal("hello from actor", result);
    }

    [Fact]
    public async Task ReplyChannel_works_with_Result_type()
    {
        var (replyAddress, replyTask) = await ReplyChannel.Open<Result<CounterState, CounterError>>();

        var okResult = Result<CounterState, CounterError>.Ok(new CounterState(42));
        await replyAddress.Tell(okResult);

        var result = await replyTask;
        Assert.True(result.IsOk);
        Assert.Equal(42, result.Value.Count);
    }

    [Fact]
    public async Task ReplyChannel_works_with_error_Result()
    {
        var (replyAddress, replyTask) = await ReplyChannel.Open<Result<CounterState, CounterError>>();

        var errResult = Result<CounterState, CounterError>.Err(new CounterError.Overflow(99, 2, 100));
        await replyAddress.Tell(errResult);

        var result = await replyTask;
        Assert.True(result.IsErr);
        Assert.IsType<CounterError.Overflow>(result.Error);
    }

    [Fact]
    public async Task ReplyChannel_supports_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var (_, replyTask) = await ReplyChannel.Open<int>(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => replyTask);
    }

    // =========================================================================
    // Envelope — command + reply address
    // =========================================================================

    [Fact]
    public async Task Envelope_carries_command_and_reply_address()
    {
        var (replyAddress, _) = await ReplyChannel.Open<Result<CounterState, CounterError>>();

        var envelope = new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(5),
            replyAddress);

        Assert.IsType<CounterCommand.Add>(envelope.Command);
        Assert.Equal(5, ((CounterCommand.Add)envelope.Command).Amount);
        Assert.Same(replyAddress, envelope.ReplyTo);
    }

    // =========================================================================
    // SpawnWithReply — envelope-aware actor with request-reply
    // =========================================================================

    [Fact]
    public async Task SpawnWithReply_processes_command_and_sends_reply()
    {
        using var cts = new CancellationTokenSource();

        Observer<CounterState, CounterEvent, CounterEffect> observer =
            (_, _, _) => PipelineResult.Ok;
        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.SpawnWithReply<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        var (replyAddress, replyTask) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);

        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(5), replyAddress));

        var result = await replyTask;

        Assert.True(result.IsOk);
        Assert.Equal(5, result.Value.Count);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task SpawnWithReply_returns_error_for_rejected_command()
    {
        using var cts = new CancellationTokenSource();

        Observer<CounterState, CounterEvent, CounterEffect> observer =
            (_, _, _) => PipelineResult.Ok;
        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.SpawnWithReply<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        var (replyAddress, replyTask) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(-1), replyAddress));

        var result = await replyTask;

        Assert.True(result.IsErr);
        Assert.IsType<CounterError.Underflow>(result.Error);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task SpawnWithReply_handles_multiple_sequential_requests()
    {
        using var cts = new CancellationTokenSource();

        Observer<CounterState, CounterEvent, CounterEffect> observer =
            (_, _, _) => PipelineResult.Ok;
        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.SpawnWithReply<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        var (reply1, task1) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(3), reply1));
        var result1 = await task1;
        Assert.True(result1.IsOk);
        Assert.Equal(3, result1.Value.Count);

        var (reply2, task2) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(7), reply2));
        var result2 = await task2;
        Assert.True(result2.IsOk);
        Assert.Equal(10, result2.Value.Count);

        var (reply3, task3) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(91), reply3));
        var result3 = await task3;
        Assert.True(result3.IsErr);
        Assert.IsType<CounterError.Overflow>(result3.Error);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task SpawnWithReply_handles_concurrent_requests()
    {
        using var cts = new CancellationTokenSource();

        Observer<CounterState, CounterEvent, CounterEffect> observer =
            (_, _, _) => PipelineResult.Ok;
        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.SpawnWithReply<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var (reply, task) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
            await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
                new CounterCommand.Add(1), reply));
            return await task;
        }).ToList();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsOk));

        var maxCount = results.Max(r => r.Value.Count);
        Assert.Equal(10, maxCount);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task SpawnWithReply_observer_is_called_for_each_transition()
    {
        using var cts = new CancellationTokenSource();
        var observedStates = new List<CounterState>();

        Observer<CounterState, CounterEvent, CounterEffect> observer = (state, _, _) =>
        {
            observedStates.Add(state);
            return PipelineResult.Ok;
        };
        Interpreter<CounterEffect, CounterEvent> interpreter =
            _ => new ValueTask<Result<CounterEvent[], PipelineError>>(
                Result<CounterEvent[], PipelineError>.Ok([]));

        var address = await Actor.SpawnWithReply<Counter, CounterState, CounterCommand,
            CounterEvent, CounterEffect, CounterError, Unit>(
                default, observer, interpreter, cancellationToken: cts.Token);

        var (reply, task) = await ReplyChannel.Open<Result<CounterState, CounterError>>(cts.Token);
        await address.Tell(new Envelope<CounterCommand, Result<CounterState, CounterError>>(
            new CounterCommand.Add(3), reply));
        await task;

        Assert.Equal(3, observedStates.Count);
        Assert.Equal(1, observedStates[0].Count);
        Assert.Equal(2, observedStates[1].Count);
        Assert.Equal(3, observedStates[2].Count);

        await cts.CancelAsync();
    }
}
