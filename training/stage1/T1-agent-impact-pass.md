# Ticket 1, step 2: the agent's impact pass

This is the agent's answer exactly as it was given, before I checked any of it. My checking is
in the impact matrix in `T1-remove-mediatr.md`.

## Session

| Item            | Value                                |
|-----------------|--------------------------------------|
| Date            | 2026-09-28, 8:45 am                  |
| Tool and model  | Claude Code, Opus 5.5                |
| Permission mode | plan (read-only, no code changes)    |

## Prompt given to the agent

```text
Read training/stage1/T1-remove-mediatr.md. It describes a change to remove MediatR from the Ordering service. Do NOT write or change any code.

List every file and component this change would touch, grouped under the 13 categories in the impact matrix table in that file. For each item give the file path and line number, and one sentence on why it is affected. Include anything that is registered or resolved at runtime, not only what the compiler would catch. At the end, list anything you are unsure about.
```

## Agent's answer in the chat (summary)

I didn't change any code.  The full list is in [its plan file, reproduced in full below], grouped under the 13 categories from the ticket, with a file and line number for every entry. Plan mode only lets me write to that one file, so that's where it went. I'm not asking you to approve the plan, because this step asks for analysis and nothing gets built yet.

The items that only fail when the app runs:

1. DbContext constructor fallback (OrderingContext.cs:21 and :27). OrderingContext has two constructors. If the new event dispatcher isn't registered, the container quietly uses the one-parameter constructor instead. The first save then throws a NullReferenceException at line 55.
2. Each command goes through the pipeline twice. IdentifiedCommandHandler.cs:87 sends the inner command through IMediator again. So logging and validation run twice, and CreateOrderCommandValidator only runs on that second pass. At TransactionBehavior.cs:27, the second pass reuses the transaction that's already open, which keeps the idempotency row and the order in one commit. If the new design skips the second pass, the validator silently stops running.
3. Exceptions are swallowed. IdentifiedCommandHandler.cs:99-102 has catch { return default; }. A validation failure reaches the endpoint as false, not as an exception.
4. Domain events are dispatched by their runtime type (MediatorExtension.cs:19). The replacement needs a lookup from event type to handler, and acceptance criterion 7 asks for a FrozenDictionary there. One domain event handler saves again partway through dispatch (ValidateOrAddBuyer...Handler.cs:47), so the new dispatcher has to handle being called again while it's already running.
5. The five RabbitMQ handlers aren't built until a message arrives (RabbitMQEventBus.cs:204). A missing registration fails on the first message, not at startup.
6. [AsParameters] on OrderServices. If a handler is added there without being registered, minimal APIs treat it as the request body instead of a service.

Things a text search gets wrong:
- BuyerPaymentMethodVerifiedDomainEvent.cs declares a class with a different name, BuyerAndPaymentMethodVerifiedDomainEvent, so searching by file name misses it.
- The .Send calls in ClientApp belong to the MVVM messenger, not MediatR.
- CreateOrderCommandHandler takes IMediator in its constructor but never uses it.

Unsure about (full list at the end of the file):
- Whether the "122 passed" baseline includes Ordering.FunctionalTests. The ticket's scope leaves that project out, but it's the only one that starts the real app and so catches the runtime failures above.
- The four identified handlers for the SetXxx commands seem to be dead code, because nothing sends those commands wrapped with a request id.
- Whether the double pipeline pass is deliberate behaviour to keep or an accident.
- I haven't checked in the MediatR 13 source how it runs domain event handlers by default.
- I haven't confirmed whether a bad [AsParameters] service fails at startup or on the first request in .NET 10.
- I haven't read CreateOrderDraftCommandHandler, the likeliest handler to finish synchronously for the ValueTask change.

For your step 3 check, the quickest route is a scratch branch: remove the MediatR package reference and the four global usings, then run dotnet build. The compiler errors should line up with categories 1, 3, 5, 6, 7, 8 and 12. The runtime items above only show up in the functional tests or when you place an order under Aspire.

## Agent's full impact list (from its plan file, unedited)

# T1 impact list: removing MediatR from Ordering (read-only analysis, no code changed)

## Context

Step 2 of `training/stage1/T1-remove-mediatr.md` asks for the agent's own impact list, grouped by
the 13 categories in that file's impact matrix. Deepak fills the "Agent said" column from this
answer and then verifies each entry himself in step 3. Every entry below comes from a file I read in
this session; line numbers are from the current `training/stage1` branch (commit 93a436f).

## 1. Domain model and domain events

- `src/Ordering.Domain/Ordering.Domain.csproj:8` - the only direct `PackageReference` to MediatR in
  the solution; API, Infrastructure and UnitTests get MediatR transitively through this reference,
  so removing it breaks all four projects at once (acceptance criterion 2).
- `src/Ordering.Domain/GlobalUsings.cs:3` - `global using MediatR;` is why no domain file mentions
  MediatR by name.
- `src/Ordering.Domain/SeedWork/Entity.cs:19,20,22,24,28` - the domain event list, the
  `DomainEvents` property, `AddDomainEvent` and `RemoveDomainEvent` are all typed as MediatR
  `INotification` and need a domain-owned marker type (for example `IDomainEvent`) instead.
- Seven domain event classes implement `INotification`:
  - `src/Ordering.Domain/Events/OrderStartedDomainEvent.cs:15`
  - `src/Ordering.Domain/Events/OrderStatusChangedToAwaitingValidationDomainEvent.cs:7`
  - `src/Ordering.Domain/Events/OrderStatusChangedToStockConfirmedDomainEvent.cs:7`
  - `src/Ordering.Domain/Events/OrderStatusChangedToPaidDomainEvent.cs:7`
  - `src/Ordering.Domain/Events/OrderShippedDomainEvent.cs:3`
  - `src/Ordering.Domain/Events/OrderCancelledDomainEvent.cs:3`
  - `src/Ordering.Domain/Events/BuyerPaymentMethodVerifiedDomainEvent.cs:4`. This file declares
    `BuyerAndPaymentMethodVerifiedDomainEvent` (line 3), so a search by file name misses it.
- The places that raise events stay unchanged, but they show what must still be dispatched:
  `Order.cs:103,112,123,139,152,177` and `Buyer.cs:38,47` (both under
  `src/Ordering.Domain/AggregatesModel/`).

## 2. Persistence and existing data

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

## 3. Application: commands, queries, handlers

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

## 4. Validation

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

## 5. Cross-cutting: logging, transactions, idempotency

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

## 6. API surface: endpoints, versioning, OpenAPI

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

## 7. Integration events and message contracts

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

## 8. Dependency injection and configuration

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

## 9. Other services and clients

- Nothing outside Ordering references MediatR. Ordering.API is referenced only by
  `src/eShop.AppHost/eShop.AppHost.csproj:25` (as an Aspire resource),
  `tests/Ordering.FunctionalTests` and `tests/Ordering.UnitTests`.
- False positives from a text search: `src/ClientApp/ViewModels/CheckoutViewModel.cs:121` and
  `CatalogItemViewModel.cs:54` call a `.Send(...)` method, but it belongs to the MVVM messenger and
  has nothing to do with MediatR.
- Other services see Ordering only through HTTP routes and RabbitMQ events, and neither changes.

## 10. Security and authorisation

- `src/Ordering.API/Program.cs:21-22` - `RequireAuthorization()` is applied at the route group, not
  inside the pipeline, so removing MediatR changes no security behaviour. No MediatR behaviour does
  authorisation.
- `CreateOrderAsync` masks the card number (`OrdersApi.cs:140`) BEFORE the command enters the
  pipeline, and `LoggingBehavior` logs the whole command with `{@Command}`. The new pipeline must keep
  receiving the masked command, never the raw `CreateOrderRequest`.

## 11. Observability

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

## 12. Tests

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

## 13. Docs

- No README or doc in the repo mentions MediatR (searched all `*.md`); only the `training/` notes do.
- Code comments that mention the mediator: `Extensions.cs:34`, `Extensions.cs:44`
  ("validator behavior"), `IdentifiedCommandHandler.cs:86`, `OrderingContext.cs:49-58`
  (domain event dispatch choices A/B).
- `src/Ordering.Infrastructure/MediatorExtension.cs` - the file name itself will be wrong after the
  change.

## Unsure about

1. **Ordering.FunctionalTests scope.** The ticket's scope names Ordering.UnitTests only, but the
   functional tests boot the real container and will break or pass on DI wiring. The "122 passed"
   baseline may or may not include them, because they need Docker for Postgres. Worth confirming
   which projects make up the 122.
2. **Dead identified handlers.** No code sends `IdentifiedCommand` for the four SetXxx commands.
   The integration handlers send plain commands, so the four SetXxx identified subclasses are
   registered but never resolved. Whether to keep them is a behaviour question, not a MediatR one.
3. **Whether the double pipeline pass is "behaviour".** Logging and validation run twice for
   identified commands today. I read that as behaviour to preserve, but it may be accidental.
4. **Domain event handler order.** MediatR 13's default publisher awaits handlers one at a time in
   registration order. Each event has exactly one handler today, so order only matters across
   events. I have not checked the MediatR 13 source to confirm the default publisher.
5. **Minimal API inference timing.** I believe an unregistered service in `[AsParameters]` fails
   when endpoints are first built. I have not confirmed whether that happens at startup or on the
   first request in .NET 10.
6. **ValueTask on handlers.** The handlers I read await EF, and `SetPaidOrderStatusCommandHandler.cs:22`
   also awaits `Task.Delay(10000)`. It is unclear which handlers "commonly complete synchronously"
   (acceptance criterion 7). `CreateOrderDraftCommandHandler` is the likeliest candidate, but I have
   not read it.
7. **Assembly-scan catch-alls.** I searched for MediatR interface names only. A generic
   `IRequestHandler` registered through a pattern that my search terms would not match seems
   unlikely but is not ruled out; a build after removing the package is the definitive check.

## Verification (for step 3)

- Remove the package reference and the four global usings on a scratch branch, then run
  `dotnet build`. The compiler error list should match categories 1, 3, 5, 6, 7, 8 and 12.
- The runtime-only items (OrderingContext constructor fallback, keyed integration handlers,
  `[AsParameters]` inference, validator scan) only show up in the functional tests or an Aspire run
  that places an order end to end.
