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

Today an endpoint sends a command through IMediator, and MediatR finds the matching handler at runtime. Afterwards the endpoint receives its handler directly, so a missing handler is caught when the application starts or builds. The existing behaviour (and functionality) of the application should remain the same. 

Out of scope:

1. Do not change the business logic inside the handler. Only change how the handler is called. 

2. Do not change the integration events that travel between services over RabbitMQ, or the event bus that carries them. These are not part of MediatR.

3. However, the five handlers in src/Ordering.API/Application/IntegrationEvents/EventHandling/ are in scope. Each one receives a RabbitMQ event and then sends a command through IMediator. Only the way they send that command changes. The event they receive stays the same.

4. Do not commit files that the build regenerates, such as the gRPC and OpenAPI files. 

5. Do not change any service other than Ordering. The change is limited to Ordering.API, Ordering.Domain, Ordering.Infrastructure and Ordering.UnitTests, plus removing MediatR from Directory.Packages.props. 

Is a text search for "MediatR" enough to find everything? Why or why not:

A text search for "MediatR" is not enough, because the files use MediatR types through the global usings without the word MediatR appearing in them. Removing the MediatR global using and building will make the compiler list every place that uses a MediatR type. 
Also runtime behaviour is to be checked when MediatR package is no longer used. Some of the functions are dynamically invoked at runtime. Have to look into src/Ordering.API/Extensions/Extensions.cs for any runtime initializations/injections settings that need to be handled by alternate path. 

## Steps 2 and 3: impact matrix

One section per category. "Agent said" comes from the agent's answer in step 2.
"Verified" says what I found when I checked, with the file and line.

### Domain model and domain events

- Agent said:
  - 7 event classes implement INotification (the 7 files and lines it named)
  - Entity.cs lines 19, 20, 22, 24, 28
  - Ordering.Domain.csproj:8 has the only MediatR reference
  - GlobalUsings.cs:3 has global using MediatR;
  - Order.cs and Buyer.cs raise the events and stay unchanged
- Verified:
  - Errors at exactly those 7files and lines files and lines
  - Errors at lines 19, 20, 22, 28
  - Not a compile error; open the file and check
  - I commented out this line to check for compiler errors
  - No errors there
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Persistence and existing data

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Application: commands, queries, handlers

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Validation

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Cross-cutting: logging, transactions, idempotency

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### API surface: endpoints, versioning, OpenAPI

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Integration events and message contracts

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Dependency injection and configuration

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Other services and clients

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Security and authorisation

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Observability

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Tests

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Docs

- Agent said:
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

## Step 4: instruction to the agent

(to be written)

## Steps 5 and 6: plan review and diff review notes

(to be written)
