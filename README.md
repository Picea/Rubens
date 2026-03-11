# Picea.Rubens

Actor model (Hewitt 1973) built on the [Picea](https://github.com/Picea/Picea) kernel.

## What is Rubens?

Rubens implements Hewitt's three axioms of the Actor Model on top of the Picea automaton kernel:

1. **Send messages** — `Address<TCommand>.Tell()` (fire-and-forget, asynchronous)
2. **Create actors** — `Actor.Spawn()` returns an unforgeable `Address<TCommand>`
3. **Designate behavior** — `Decider.Decide + Automaton.Transition` (the new state IS the new behavior)

All actors are **Deciders**: they validate commands against state, produce events, and evolve via transitions. This is a domain design choice — actors model business logic, not raw message pipes.

## Key Types

| Type | Description |
|------|-------------|
| `Actor` | Factory for spawning actors (Hewitt's axiom #2) |
| `Address<TCommand>` | Unforgeable capability to send commands (axiom #1) |
| `Envelope<TCommand, TReply>` | Command + reply address (Hewitt's request-reply) |
| `ReplyActor<T>` | One-shot Decider for request-reply (axiom #3) |
| `ReplyChannel` | Convenience factory to spawn reply actors |

## Installation

```bash
dotnet add package Picea.Rubens
```

## Quick Start

```csharp
using Picea;
using Picea.Rubens;

// Spawn an actor — returns an unforgeable address
var address = await Actor.Spawn<MyDecider, MyState, MyCommand,
    MyEvent, MyEffect, MyError, Unit>(
    default, observer, interpreter, cancellationToken: ct);

// Send a command (fire-and-forget)
await address.Tell(new MyCommand.DoSomething());

// Request-reply via envelope pattern
var replyAddress = await Actor.SpawnWithReply<MyDecider, MyState, MyCommand,
    MyEvent, MyEffect, MyError, Unit>(
    default, observer, interpreter, cancellationToken: ct);

var (reply, task) = await ReplyChannel.Open<Result<MyState, MyError>>(ct);
await replyAddress.Tell(new Envelope<MyCommand, Result<MyState, MyError>>(
    new MyCommand.DoSomething(), reply));
var result = await task;
```

## Design

The key insight is that Hewitt's "designate behavior for next message" is exactly what a Mealy machine does: `Transition(state, event) → (state', effect)`. The new state IS the new behavior. This is why Hewitt's actors and finite-state machines are the same mathematical object — both are coalgebras over the functor `F(X) = (Output × X)^Input`.

## References

- Hewitt, Bishop, Steiger (1973). "A Universal Modular Actor Formalism for Artificial Intelligence." IJCAI'73.
- Hewitt (2010). "Actor Model of Computation: Scalable Robust Information Systems." arXiv:1008.1459.
- Chassaing, Jérémie (2021). "The Decider Pattern."

## License

[Apache-2.0](LICENSE)
