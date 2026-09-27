# Ticket 1: remove MediatR from the Ordering service

## Requirement

Replace MediatR in the Ordering service with a native, compile-time safe CQRS mechanism using
plain C# and Microsoft.Extensions.DependencyInjection. Behaviour must not change.

## Acceptance criteria

1. No project in the solution references the MediatR package, and Directory.Packages.props no
   longer pins it.
2. Ordering.Domain has no dependency on any messaging or mediator library.
3. A missing handler is a compile error or a startup failure, never a failure on the first
   request. Endpoints receive their handler through a generic interface, such as
   `ICommandHandler<TCommand, TResult>`, not through a runtime type lookup.
4. The logging, validation and transaction behaviour still wraps every command, in the same
   order as before.
5. Idempotency is preserved: a command whose request id has already been processed is still
   not processed twice.
6. Domain events are still dispatched at the same point in the unit of work as before.
7. Handlers return `ValueTask` where they commonly complete synchronously. `FrozenDictionary`
   is used where a lookup table is built once at startup and only read after that.
8. All tests pass without being weakened (baseline: 122 passed), and the application runs under
   Aspire and places an order from start to finish.

## Step 1: restatement

What the change is:

(to be written)

Out of scope:

(to be written)

Is a text search for "MediatR" enough to find everything? Why or why not:

(to be written)

## Steps 2 and 3: impact matrix

"Agent said" is filled from the agent's answer in step 2. "Verified" gives the file and line
I checked.

| Category                                          | Agent said | Verified (file:line) | Agent missed | Wrong or out of scope | Test that proves it |
|---------------------------------------------------|------------|----------------------|--------------|-----------------------|---------------------|
| Domain model and domain events                    |            |                      |              |                       |                     |
| Persistence and existing data                     |            |                      |              |                       |                     |
| Application: commands, queries, handlers          |            |                      |              |                       |                     |
| Validation                                        |            |                      |              |                       |                     |
| Cross-cutting: logging, transactions, idempotency |            |                      |              |                       |                     |
| API surface: endpoints, versioning, OpenAPI       |            |                      |              |                       |                     |
| Integration events and message contracts          |            |                      |              |                       |                     |
| Dependency injection and configuration            |            |                      |              |                       |                     |
| Other services and clients                        |            |                      |              |                       |                     |
| Security and authorisation                        |            |                      |              |                       |                     |
| Observability                                     |            |                      |              |                       |                     |
| Tests                                             |            |                      |              |                       |                     |
| Docs                                              |            |                      |              |                       |                     |

## Step 4: instruction to the agent

(to be written)

## Steps 5 and 6: plan review and diff review notes

(to be written)
