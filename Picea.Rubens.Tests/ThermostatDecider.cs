// =============================================================================
// Thermostat Decider — Smart Thermostat Test Domain Logic
// =============================================================================

using System.Diagnostics;

using Picea;

namespace Picea.Rubens.Tests;

// ── State ─────────────────────────────────────────────────

public record ThermostatState(
    decimal CurrentTemp,
    decimal TargetTemp,
    bool Heating,
    bool Active);

// ── Commands ───────────────────────────────────────────────

public interface ThermostatCommand
{
    record struct RecordReading(decimal Temperature) : ThermostatCommand;
    record struct SetTarget(decimal Target) : ThermostatCommand;
    record struct Shutdown : ThermostatCommand;
}

// ── Events ────────────────────────────────────────────────

public interface ThermostatEvent
{
    record struct TemperatureRecorded(decimal Temperature) : ThermostatEvent;
    record struct TargetSet(decimal Target) : ThermostatEvent;
    record struct HeaterTurnedOn : ThermostatEvent;
    record struct HeaterTurnedOff : ThermostatEvent;
    record struct AlertRaised(string Message) : ThermostatEvent;
    record struct ShutdownCompleted : ThermostatEvent;
}

// ── Errors ────────────────────────────────────────────────

public interface ThermostatError
{
    record struct InvalidTarget(decimal Target, decimal Min, decimal Max) : ThermostatError;
    record struct SystemInactive : ThermostatError;
    record struct AlreadyShutdown : ThermostatError;
}

// ── Effects ───────────────────────────────────────────────

public interface ThermostatEffect
{
    record struct None : ThermostatEffect;
    record struct ActivateHeater : ThermostatEffect;
    record struct DeactivateHeater : ThermostatEffect;
    record struct SendNotification(string Message) : ThermostatEffect;
}

// ── Decider ───────────────────────────────────────────────

public class Thermostat
    : Decider<ThermostatState, ThermostatCommand, ThermostatEvent, ThermostatEffect, ThermostatError, Unit>
{
    public const decimal MinTarget = 5.0m;
    public const decimal MaxTarget = 40.0m;
    public const decimal AlertThreshold = 35.0m;

    public static (ThermostatState State, ThermostatEffect Effect) Initialize(Unit _) =>
        (new ThermostatState(
            CurrentTemp: 20.0m,
            TargetTemp: 22.0m,
            Heating: false,
            Active: true),
         new ThermostatEffect.None());

    public static Result<ThermostatEvent[], ThermostatError> Decide(
        ThermostatState state, ThermostatCommand command) =>
        command switch
        {
            ThermostatCommand.Shutdown when !state.Active =>
                Result<ThermostatEvent[], ThermostatError>
                    .Err(new ThermostatError.AlreadyShutdown()),

            _ when !state.Active =>
                Result<ThermostatEvent[], ThermostatError>
                    .Err(new ThermostatError.SystemInactive()),

            ThermostatCommand.RecordReading(var temp) when temp > AlertThreshold =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok(state.Heating
                        ? [new ThermostatEvent.TemperatureRecorded(temp),
                           new ThermostatEvent.HeaterTurnedOff(),
                           new ThermostatEvent.AlertRaised(
                               $"Temperature {temp}°C exceeds alert threshold {AlertThreshold}°C")]
                        : [new ThermostatEvent.TemperatureRecorded(temp),
                           new ThermostatEvent.AlertRaised(
                               $"Temperature {temp}°C exceeds alert threshold {AlertThreshold}°C")]),

            ThermostatCommand.RecordReading(var temp) when temp < state.TargetTemp && !state.Heating =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TemperatureRecorded(temp),
                         new ThermostatEvent.HeaterTurnedOn()]),

            ThermostatCommand.RecordReading(var temp) when temp >= state.TargetTemp && state.Heating =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TemperatureRecorded(temp),
                         new ThermostatEvent.HeaterTurnedOff()]),

            ThermostatCommand.RecordReading(var temp) =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TemperatureRecorded(temp)]),

            ThermostatCommand.SetTarget(var target) when target is < MinTarget or > MaxTarget =>
                Result<ThermostatEvent[], ThermostatError>
                    .Err(new ThermostatError.InvalidTarget(target, MinTarget, MaxTarget)),

            ThermostatCommand.SetTarget(var target) when state.CurrentTemp < target && !state.Heating =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TargetSet(target),
                         new ThermostatEvent.HeaterTurnedOn()]),

            ThermostatCommand.SetTarget(var target) when state.CurrentTemp >= target && state.Heating =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TargetSet(target),
                         new ThermostatEvent.HeaterTurnedOff()]),

            ThermostatCommand.SetTarget(var target) =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.TargetSet(target)]),

            ThermostatCommand.Shutdown when state.Heating =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.HeaterTurnedOff(),
                         new ThermostatEvent.ShutdownCompleted()]),

            ThermostatCommand.Shutdown =>
                Result<ThermostatEvent[], ThermostatError>
                    .Ok([new ThermostatEvent.ShutdownCompleted()]),

            _ => throw new UnreachableException()
        };

    public static (ThermostatState State, ThermostatEffect Effect) Transition(
        ThermostatState state, ThermostatEvent @event) =>
        @event switch
        {
            ThermostatEvent.TemperatureRecorded(var temp) =>
                (state with { CurrentTemp = temp },
                 new ThermostatEffect.None()),

            ThermostatEvent.TargetSet(var target) =>
                (state with { TargetTemp = target },
                 new ThermostatEffect.None()),

            ThermostatEvent.HeaterTurnedOn =>
                (state with { Heating = true },
                 new ThermostatEffect.ActivateHeater()),

            ThermostatEvent.HeaterTurnedOff =>
                (state with { Heating = false },
                 new ThermostatEffect.DeactivateHeater()),

            ThermostatEvent.AlertRaised(var message) =>
                (state,
                 new ThermostatEffect.SendNotification(message)),

            ThermostatEvent.ShutdownCompleted =>
                (state with { Active = false },
                 new ThermostatEffect.SendNotification("Thermostat shut down")),

            _ => throw new UnreachableException()
        };

    public static bool IsTerminal(ThermostatState state) => !state.Active;
}
