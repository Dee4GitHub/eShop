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
    - `src/Ordering.Infrastructure/OrderingContext.cs:18,27-30` - the DbContext takes `IMediator` in
      its second constructor. The DI container picks the constructor with the most parameters it can
      resolve, so if the replacement dispatcher is not registered, DI silently falls back to the
      one-parameter constructor at line 21. `_mediator` is then null, and line 55 throws a
      NullReferenceException on the first save. The compiler cannot catch this failure.
    - `src/Ordering.Infrastructure/OrderingContext.cs:55` - `SaveEntitiesAsync` dispatches domain events
      BEFORE `base.SaveChangesAsync` (line 59). That position, inside the same transaction, is the
      "same point in the unit of work" that acceptance criterion 6 protects.
    - `src/Ordering.Infrastructure/MediatorExtension.cs:5,19` - `DispatchDomainEventsAsync` calls
      `mediator.Publish(domainEvent)`. The event's static type is `INotification`, so MediatR finds the
      handler from the runtime type. The replacement needs a runtime type to handler lookup built once at
      startup, which is where acceptance criterion 7 asks for `FrozenDictionary`.
    - `src/Ordering.Infrastructure/GlobalUsings.cs:2` - the MediatR global using for Infrastructure.
    - `src/Ordering.Infrastructure/EntityConfigurations/OrderEntityTypeConfiguration.cs:9`,
      `BuyerEntityTypeConfiguration.cs:10`, `PaymentMethodEntityTypeConfiguration.cs:10`,
      `OrderItemEntityTypeConfiguration.cs:10` - each one calls `Ignore(b => b.DomainEvents)`. These
      lines keep compiling if the element type changes, but the EF model must stay unchanged.
    - `src/Ordering.Infrastructure/Migrations/OrderingContextModelSnapshot.cs` - no schema change is
      expected. A `dotnet ef migrations add` should produce an empty `Up()`, and that result proves the
      EF model did not move.
    - `src/Ordering.Infrastructure/Idempotency/RequestManager.cs:21-37` and the `requests` table
      (`ClientRequestEntityTypeConfiguration.cs:8`) - existing rows stay valid. `Name` stores
      `typeof(T).Name` (line 30), so renaming a command class would change what new rows record.  
- Verified:
    - Build check. I commented out `global using MediatR;` in `src/Ordering.Infrastructure/GlobalUsings.cs` and rebuilt. The Infrastructure errors were:
      - `MediatorExtension.cs` line 5: `IMediator` not found.
      - `OrderingContext.cs` line 18: `IMediator` not found (the `_mediator` field).
      - `OrderingContext.cs` line 27: `IMediator` not found (the second constructor's parameter).
      - These match the agent's line numbers. The build does not report line 19 of `MediatorExtension.cs` separately, because the error on line 5 already covers the type.
    - Runtime check, by reading `OrderingContext.cs`:
      - Line 21 is a constructor that takes only `DbContextOptions<OrderingContext>`.
      - Lines 27-29 are a second constructor that also takes `IMediator` and stores it in `_mediator`.
      - Line 55 calls `_mediator.DispatchDomainEventsAsync(this)` with no null check, before `base.SaveChangesAsync`.
      - The agent's warning is correct. If the replacement dispatcher is not registered, dependency injection can use the one-parameter constructor, `_mediator` stays null, and the first save fails at line 55. The build cannot catch this.
    - Not checked yet: the four `Ignore(b => b.DomainEvents)` lines in `EntityConfigurations`, the migration snapshot, and `RequestManager.cs`. None of them is a build error, so each needs reading or the empty-migration check.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - `Ordering.FunctionalTests`: placing an order runs the first save, so it fails if the dispatcher is missing.
    - `dotnet ef migrations add` producing an empty `Up()` proves the EF model did not change.

### Application: commands, queries, handlers

- Agent said:
    - Commands implementing `IRequest<T>` (each needs a new marker or none):
      `CancelOrderCommand.cs:3`, `CreateOrderCommand.cs:17`, `CreateOrderDraftCommand.cs:4`,
      `SetAwaitingValidationOrderStatusCommand.cs:3`, `SetPaidOrderStatusCommand.cs:3`,
      `SetStockConfirmedOrderStatusCommand.cs:3`, `SetStockRejectedOrderStatusCommand.cs:3`,
      `ShipOrderCommand.cs:3`, all in `src/Ordering.API/Application/Commands/`.
    - `src/Ordering.API/Application/Commands/IdentifiedCommand.cs:3-4` - the generic wrapper is itself
      an `IRequest<R>`, and its constraint `where T : IRequest<R>` has to change as well.
    - Handlers implementing `IRequestHandler<,>` (the move to `ValueTask` in acceptance criterion 7
      applies to these):
      `CancelOrderCommandHandler.cs:4`, `CreateOrderCommandHandler.cs:7`,
      `CreateOrderDraftCommandHandler.cs:8`, `SetAwaitingValidationOrderStatusCommandHandler.cs:4`,
      `SetPaidOrderStatusCommandHandler.cs:4`, `SetStockConfirmedOrderStatusCommandHandler.cs:4`,
      `SetStockRejectedOrderStatusCommandHandler.cs:4`, `ShipOrderCommandHandler.cs:4`.
    - `src/Ordering.API/Application/Commands/IdentifiedCommandHandler.cs:9,10,12,17,87` - the abstract
      idempotency base class. Line 87 re-sends the inner command through `IMediator`, which means the
      inner command passes through the whole behaviour pipeline a SECOND time (see category 5).
    - Seven `IdentifiedCommandHandler` subclasses take `IMediator` in their constructors:
      `CancelOrderCommandHandler.cs:34,37`, `CreateOrderCommandHandler.cs:57,60`,
      `ShipOrderCommandHandler.cs:34,37`, `SetAwaitingValidationOrderStatusCommandHandler.cs:34,37`,
      `SetPaidOrderStatusCommandHandler.cs:37,40`, `SetStockConfirmedOrderStatusCommandHandler.cs:37,40`,
      `SetStockRejectedOrderStatusCommandHandler.cs:38,41`.
    - `src/Ordering.API/Application/Commands/CreateOrderCommandHandler.cs:11,16,24` - takes `IMediator`,
      stores it, and never uses it. Removing the constructor parameter changes the constructor signature
      that `NewOrderCommandHandlerTest.cs:41` calls.
    - Seven domain event handlers implement `INotificationHandler<>`, all in
      `src/Ordering.API/Application/DomainEventHandlers/`:
      `ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler.cs:4`,
      `UpdateOrderWhenBuyerAndPaymentMethodVerifiedDomainEventHandler.cs:3`,
      `OrderStatusChangedToAwaitingValidationDomainEventHandler.cs:4`,
      `OrderStatusChangedToStockConfirmedDomainEventHandler.cs:4`,
      `OrderStatusChangedToPaidDomainEventHandler.cs:3`, `OrderShippedDomainEventHandler.cs:4`,
      `OrderCancelledDomainEventHandler.cs:4`.
    - `ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler.cs:47-48` - this handler calls
      `SaveEntitiesAsync` from inside a dispatch. The Buyer events it raises are then dispatched
      recursively, and the replacement dispatcher must allow that re-entry.
    - Queries: `IOrderQueries` / `OrderQueries` never go through MediatR, because the endpoints call them
      directly at `OrdersApi.cs:84,96,102`. The query side is not affected.
- Verified:
    - Build check. I commented out `global using MediatR;` in
      `src/Ordering.API/GlobalUsings.cs` and rebuilt Ordering.API.
    - The 8 commands, 8 handlers and 7 domain event handlers errored at the agent's lines.
    - `IdentifiedCommand.cs` lines 3 and 4, and `IdentifiedCommandHandler.cs` lines 9, 10, 12 and 17, errored.
    - Each of the 7 subclasses also errored at its `base(...)` call, and
      `IdentifiedCommandHandler.cs` errored at line 39. These follow from the
      `where T : IRequest<R>` constraint the agent named.
    - No errors in `OrderQueries` or `OrdersApi.cs`. Queries are not affected.
    - Runtime check, by reading:
      - `IdentifiedCommandHandler.cs` line 87 is `await _mediator.Send(command, ...)`.
      - `CreateOrderCommandHandler.cs` line 24 stores `_mediator`, and nothing else uses it.
      - `ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler.cs` lines 47-48 call
        `SaveEntitiesAsync` inside the handler.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### Validation

- Agent said:
    - `src/Ordering.API/Application/Behaviors/ValidatorBehavior.cs:3,14` - implements
      `IPipelineBehavior` and resolves `IEnumerable<IValidator<TRequest>>` at runtime.
    - `src/Ordering.API/Extensions/Extensions.cs:45` - `AddValidatorsFromAssemblyContaining` finds the
      validators by assembly scan, so a validator whose type does not match the new request type is
      simply never run, and nothing fails to report it.
    - Validators: `CancelOrderCommandValidator.cs:3`, `CreateOrderCommandValidator.cs:2`,
      `ShipOrderCommandValidator.cs:3`, and `IdentifiedCommandValidator.cs:3`, which targets the closed
      type `IdentifiedCommand<CreateOrderCommand, bool>` (all in `Application/Validations/`).
    - Runtime effect: on create order, `IdentifiedCommandValidator` runs on the outer send and
      `CreateOrderCommandValidator` runs on the inner re-send at `IdentifiedCommandHandler.cs:87`. If the
      new design does not re-enter the pipeline for the inner command, `CreateOrderCommandValidator`
      stops running and no test would fail.
    - `IdentifiedCommandHandler.cs:99-102` - `catch { return default; }` swallows the
      `OrderingDomainException` that the inner validation throws, so the endpoint receives `false`
      rather than an exception. Acceptance criterion 4 requires that swallowing to remain.
- Verified:
    - Build check (same Ordering.API build as Application):
      - `ValidatorBehavior.cs` lines 3 and 14 errored.
      - `IdentifiedCommandValidator.cs` line 3 errored (CS0311, the closed `IdentifiedCommand<CreateOrderCommand, bool>` type).
      - `CancelOrderCommandValidator`, `CreateOrderCommandValidator` and `ShipOrderCommandValidator`
        did not error. They depend on FluentValidation, not MediatR.
    - Runtime check, by reading:
      - `Extensions.cs` line 45 registers validators by assembly scan. A validator that no longer
        matches a request type is skipped with no error.
      - `IdentifiedCommandHandler.cs` lines 99-102 are `catch { return default; }`.
      - The agent's point that `CreateOrderCommandValidator` only runs on the second pass
        (line 87) is not proven by reading. The test below proves it.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - Send an invalid create order (for example an expired card) through the endpoint. It must
      still be rejected after the change.

### Cross-cutting: logging, transactions, idempotency

- Agent said:
    - `src/Ordering.API/Application/Behaviors/LoggingBehavior.cs:2,7`,
      `TransactionBehavior.cs:5,20`, `ValidatorBehavior.cs:3,14` - all three implement
      `IPipelineBehavior<,>` and use `RequestHandlerDelegate<>`.
    - `src/Ordering.API/Extensions/Extensions.cs:39-41` - behaviour ORDER comes from registration
      order: Logging (outermost), then Validator, then Transaction (innermost). That registration order
      is the order acceptance criterion 4 requires.
    - `TransactionBehavior.cs:27-30` - the inner re-send skips opening a transaction because
      `HasActiveTransaction` is already true. As a result, the idempotency row written by
      `RequestManager` and the command's changes commit in ONE transaction.
    - `TransactionBehavior.cs:52` - integration events are published from the outbox after commit,
      inside the execution strategy; `OrderingIntegrationEventService.cs:40` depends on
      `GetCurrentTransaction()` from this same transaction.
    - `TransactionBehavior.cs` also wraps `CreateOrderDraftCommand`, which writes nothing, so today
      a draft request opens and commits an empty transaction. Preserving behaviour means keeping that
      transaction.
    - Idempotency: `IdentifiedCommandHandler.cs:41-48` plus
      `src/Ordering.Infrastructure/Idempotency/RequestManager.cs`. Only the three HTTP commands use it
      (cancel, ship, create). The integration event handlers send plain commands with no request id.
- Verified:
- Agent missed:
- Wrong or out of scope:
- Test that proves it:

### API surface: endpoints, versioning, OpenAPI

- Agent said:
    - `src/Ordering.API/Apis/OrderServices.cs:2,7` - the `[AsParameters]` bag exposes `IMediator` as a
      settable property.
    - `src/Ordering.API/Apis/OrdersApi.cs:41,70,115,155` - the four `services.Mediator.Send(...)` calls
      (cancel, ship, draft, create). The endpoints need to receive typed handlers here (acceptance
      criterion 3).
    - Runtime risk: `OrderServices` is bound with `[AsParameters]`, so each constructor parameter is
      treated as a DI service only if the container reports it as registered. If a new handler interface
      is added to `OrderServices` without being registered, minimal APIs infer it as a request body and
      the failure appears when endpoints are built, not at compile time.
    - Routes, the version (`HasApiVersion(1.0)` at line 9) and the response types should not change,
      so the OpenAPI document should come out identical. Per the ticket, do not commit the regenerated
      OpenAPI file.
- Verified:
    - Build check (same Ordering.API build): `OrderServices.cs` lines 2 and 7 errored.
    - `OrdersApi.cs` lines 41, 70, 115 and 155 did not error, but each is
      `services.Mediator.Send(...)`. The error on the `Mediator` property hides them until
      `OrderServices` changes.
    - `OrdersApi.cs` line 9 is `HasApiVersion(1.0)`.
    - The `[AsParameters]` risk is a startup failure, so the build and reading cannot prove it.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - Ordering.FunctionalTests must pass. They start the app, so an unregistered handler on
      `OrderServices` fails there.
    - Run the AppHost and open the Ordering OpenAPI page. Routes and version are unchanged.

### Integration events and message contracts

- Agent said:
    - Five RabbitMQ handlers take `IMediator` and call `mediator.Send`, all in
      `src/Ordering.API/Application/IntegrationEvents/EventHandling/`:
      `GracePeriodConfirmedIntegrationEventHandler.cs:4,28`,
      `OrderPaymentFailedIntegrationEventHandler.cs:4,21`,
      `OrderPaymentSucceededIntegrationEventHandler.cs:4,21`,
      `OrderStockConfirmedIntegrationEventHandler.cs:4,21`,
      `OrderStockRejectedIntegrationEventHandler.cs:3,24`.
    - Runtime detail: these handlers are registered as keyed transients
      (`src/EventBus/Extensions/EventBusBuilderExtensions.cs:27`) and resolved only when a message arrives
      (`src/EventBusRabbitMQ/RabbitMQEventBus.cs:204`). A missing dependency therefore surfaces as a
      failure on the first message, not at startup, unless DI validation is switched on.
    - The event contracts (`Application/IntegrationEvents/Events/`) and the EventBus projects do not
      change (ticket, out-of-scope item 2).
- Verified:
    - Build check (same Ordering.API build): the 5 handlers errored at the constructor lines the
      agent gave (4, 4, 4, 4, 3).
    - The `mediator.Send` lines (28, 21, 21, 21, 24) did not error. The constructor error hides
      them. Reading confirms each is `await mediator.Send(command);`.
    - `EventBusBuilderExtensions.cs` line 27 registers handlers as keyed transients.
    - `RabbitMQEventBus.cs` line 204 resolves them per message with `GetKeyedServices`, so a
      missing dependency fails on the first message, not at startup.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - Run the AppHost and place an order. It must reach Paid or Cancelled, which needs the
      stock and payment events to flow through these 5 handlers.

### Dependency injection and configuration

- Agent said:
    - `src/Ordering.API/Extensions/Extensions.cs:35-42` - `AddMediatR` with
      `RegisterServicesFromAssemblyContaining(typeof(Program))` registers every handler, both kinds of
      handler (request and notification) and `IMediator` itself by reflection. Every one of those
      registrations has to be replaced by explicit ones.
    - `Extensions.cs:34` - the "Configure mediatR" comment.
    - `src/Ordering.API/GlobalUsings.cs:4` - the MediatR global using for the API project.
    - `Directory.Packages.props:95-96` - the `MediatR` 13.0.0 pin and its "Before license change"
      comment (acceptance criterion 1).
    - `Extensions.cs:12-18` - the comment explains that DbContext pooling is off because
      `OrderingContext` has two constructors. That explanation still holds only if the new dispatcher
      keeps a two-parameter constructor.
    - No MediatR license key or config entry exists (searched `*.json` under `src/Ordering.*`), so no
      appsettings change is needed.
- Verified:
    - Build check (same Ordering.API build): `Extensions.cs` lines 34-42 did not error.
      `AddMediatR` still compiles while the MediatR package is referenced, so the build only
      catches it after the package is removed.
    - By reading:
      - `Extensions.cs` lines 35-42: `AddMediatR`, the assembly scan and the 3 behaviors.
      - `Extensions.cs` lines 12-18: the pooling comment about the two constructors.
      - `Directory.Packages.props` lines 95-96: the "Before license change" comment and the
        MediatR 13.0.0 pin.
      - `GlobalUsings.cs` line 4: the MediatR global using.
    - No MediatR or license key entry in any `*.json` under the Ordering projects.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - `git grep -n MediatR` returns nothing after the change.
    - Ordering.FunctionalTests pass, so every handler is registered.

### Other services and clients

- Agent said:
    - Nothing outside Ordering references MediatR. Ordering.API is referenced only by
      `src/eShop.AppHost/eShop.AppHost.csproj:25` (as an Aspire resource),
      `tests/Ordering.FunctionalTests` and `tests/Ordering.UnitTests`.
    - False positives from a text search: `src/ClientApp/ViewModels/CheckoutViewModel.cs:121` and
      `CatalogItemViewModel.cs:54` call a `.Send(...)` method, but it belongs to the MVVM messenger and
      has nothing to do with MediatR.
    - Other services see Ordering only through HTTP routes and RabbitMQ events, and neither changes.
- Verified:
    - `git grep -l "MediatR\|IMediator"` (excluding training/) lists only files under
      `src/Ordering.*`, `tests/Ordering.UnitTests` and `Directory.Packages.props`.
    - `git grep -n "Ordering.API.csproj"` shows 3 references: `eShop.AppHost.csproj` line 25,
      `Ordering.FunctionalTests.csproj` line 19, `Ordering.UnitTests.csproj` line 20.
    - `CheckoutViewModel.cs` line 121 and `CatalogItemViewModel.cs` line 54 send
      `ProductCountChangedMessage`, the MVVM messenger. Not MediatR.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - The full `dotnet test --solution eShop.Web.slnf` passes (122 tests).

### Security and authorisation

- Agent said:
    - `src/Ordering.API/Program.cs:21-22` - `RequireAuthorization()` is applied at the route group, not
      inside the pipeline, so removing MediatR changes no security behaviour. No MediatR behaviour does
      authorisation.
    - `CreateOrderAsync` masks the card number (`OrdersApi.cs:140`) BEFORE the command enters the
      pipeline, and `LoggingBehavior` logs the whole command with `{@Command}`. The new pipeline must keep
      receiving the masked command, never the raw `CreateOrderRequest`.
- Verified:
    - `Program.cs` lines 20-21 apply `RequireAuthorization()` at the route group.
    - `OrdersApi.cs` line 140 masks the card number, and line 143 passes the masked value
      into `CreateOrderCommand`.
    - `LoggingBehavior.cs` line 9 logs the whole command with `{@Command}`.
- Agent missed:
    - `OrdersApi.cs` line 144 passes `CardSecurityNumber` (CVV) into the command unmasked, and
      `CreateOrderCommand.cs` line 53 exposes it as a public property. If the log provider
      expands `{@Command}`, the CVV is logged. To check. Existing behaviour, out of scope for T1.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - Place an order, then search the Ordering logs in the Aspire dashboard for the CVV value.

### Observability

- Agent said:
    - `LoggingBehavior.cs:9,11`, `ValidatorBehavior.cs:18,30`, `TransactionBehavior.cs:39,41,45,59` -
      log messages and the `TransactionContext` scope. Because of the nested send, "Handling command"
      is logged twice for each identified command today, and matching today's logs means keeping both
      entries.
    - `IdentifiedCommandHandler.cs:79-95` - the "Sending command" and "Command result" logs around the
      inner send.
    - `src/eShop.ServiceDefaults/Extensions.cs:64,77` - OpenTelemetry registers no MediatR source or
      meter, so no trace or metric configuration changes.
    - `OrderingContext.cs:32` - `Debug.WriteLine` in the constructor being changed. It is harmless, but
      it sits inside the edited constructor.
- Verified:
    - By reading, every line the agent cited matches:
      - `LoggingBehavior.cs` lines 9 and 11: "Handling command" and "handled" logs.
      - `ValidatorBehavior.cs` lines 18 and 30: "Validating" log and validation-errors warning.
      - `TransactionBehavior.cs` lines 39, 41, 45 and 59: the `TransactionContext` scope, begin,
        commit and error logs.
      - `IdentifiedCommandHandler.cs` lines 79-81: the "Sending command" log.
      - `ServiceDefaults/Extensions.cs` lines 64 and 77: only the AI meter and source.
        Nothing for MediatR.
      - `OrderingContext.cs` line 32: `Debug.WriteLine` in the constructor.
    - Not proven by reading: "Handling command" logged twice per identified command.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - Place an order before and after the change. In the Aspire dashboard, filter the Ordering
      logs on "Handling command" and compare the count.

### Tests

- Agent said:
    - `tests/Ordering.UnitTests/GlobalUsings.cs:6` - the MediatR global using.
    - `tests/Ordering.UnitTests/Application/OrdersWebApiTest.cs:11,18,28,43,58,74,160,173` and the
      `new OrderServices(_mediatorMock, ...)` calls at 32,47,62,78,98,115,134,162,175 - the tests mock
      `IMediator.Send` for each endpoint.
    - `OrdersWebApiTest.cs:74` - `Ship_order_bad_request` stubs
      `IdentifiedCommand<CreateOrderCommand, bool>` on a SHIP test. The test passes only because the
      empty GUID returns before the send. That existing quirk should not be "fixed" by accident.
    - `tests/Ordering.UnitTests/Application/IdentifiedCommandHandlerTest.cs:7,13,27,31,36,49,53,57` -
      the test asserts on `Send(Arg.Any<IRequest<bool>>())`. Its assertions must keep the same strength
      (sent once when new, never sent when duplicate) against the new inner handler.
    - `tests/Ordering.UnitTests/Application/NewOrderCommandHandlerTest.cs:11,20,41` - passes an
      `IMediator` into the `CreateOrderCommandHandler` constructor.
    - `tests/Ordering.UnitTests/Domain/OrderAggregateTest.cs:149,173` - these calls use
      `AddDomainEvent`/`RemoveDomainEvent`. They need no edit if the events keep their names, but they
      depend on the `Entity.cs` type change.
    - `tests/Ordering.FunctionalTests/OrderingApiFixture.cs:9` and `OrderingApiTests.cs:46-196` - boots
      the REAL DI container via `WebApplicationFactory<Program>`, so this project is the one that
      catches runtime registration failures. The ticket's scope list omits it (see "Unsure" 1).
    - No existing test covers behaviour order, domain event dispatch timing, or the recursive dispatch,
      so acceptance criteria 4 and 6 have no test proving them today.
- Verified:
    - Build check. I restored the Ordering.API using, commented out `global using MediatR;` in
      `tests/Ordering.UnitTests/GlobalUsings.cs` and rebuilt. 3 errors, one per field:
      `IdentifiedCommandHandlerTest.cs` line 7, `OrdersWebApiTest.cs` line 11,
      `NewOrderCommandHandlerTest.cs` line 11. The other lines the agent named are hidden
      behind these field errors.
    - By reading:
      - `OrdersWebApiTest.cs` line 74 stubs `IdentifiedCommand<CreateOrderCommand, bool>` in the
        ship test, and line 79 passes `Guid.Empty`. The quirk is real.
      - `IdentifiedCommandHandlerTest.cs` line 36 asserts `Received().Send`, line 57 asserts
        `DidNotReceive().Send`.
      - `OrderingApiFixture.cs` line 9 uses `WebApplicationFactory<Program>`, the real DI container.
      - `OrderAggregateTest.cs` lines 149 and 173 call `AddDomainEvent`.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - `dotnet test --solution eShop.Web.slnf` passes with 122 tests, the baseline count.

### Docs

- Agent said:
    - No README or doc in the repo mentions MediatR (searched all `*.md`); only the `training/` notes do.
    - Code comments that mention the mediator: `Extensions.cs:34`, `Extensions.cs:44`
      ("validator behavior"), `IdentifiedCommandHandler.cs:86`, `OrderingContext.cs:49-58`
      (domain event dispatch choices A/B).
    - `src/Ordering.Infrastructure/MediatorExtension.cs` - the file name itself will be wrong after the
      change.
- Verified:
    - `git grep -il mediatr -- "*.md" ":!training"` returns nothing. No doc mentions MediatR.
    - By reading:
      - `Extensions.cs` line 34: "Configure mediatR".
      - `Extensions.cs` line 44: "validator behavior".
      - `IdentifiedCommandHandler.cs` line 86: "Send the embedded business command to mediator".
      - `OrderingContext.cs` lines 49-58: the choice A and B comment above the dispatch call.
      - `MediatorExtension.cs`: the file name itself.
- Agent missed:
- Wrong or out of scope:
- Test that proves it:
    - `git grep -in mediat` returns only the `training/` notes after the change.

## Step 4: instruction to the agent

(to be written)

## Steps 5 and 6: plan review and diff review notes

(to be written)
