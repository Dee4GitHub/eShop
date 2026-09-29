# T1 Run 1: tests only (plan v2, updated with Deepak's answers)

## Context

Ticket 1 removes MediatR from Ordering. Part 5 of "Step 4: instruction to the agent" in
`training/stage1/T1-remove-mediatr.md` splits the work into two runs. Run 1 adds tests that pass
on today's MediatR code; Run 2 is the refactor, and those tests must then pass without being
changed. This plan covers Run 1 only. Nothing is written until Deepak approves.

## New items raised by the answers and the updated file (read first)

1. **Part 5 of the instruction file still has the old text.** The re-read shows that only Part 3
   line 503 changed. These lines still disagree with the answers:
   - Line 519: "Do not change any code under `src/`". This conflicts with answer 7, the mutation
     check.
   - Line 525: "post a create order with an expired card". This conflicts with answer 1.
   - Line 526: "unit test: register three fake behaviors". This conflicts with answer 3.
   - Part 4 has no bullet saying the four behaviour log templates must stay exactly as they are
     (answer 3).
   Run 1 follows the answers. Run 2 will be read cold, though, and if these lines are not updated
   first, Run 2 could follow the old text: it could add the fake unit test, or change a log
   message and break test 2. I recommend updating lines 519, 525 and 526, and adding the
   log-template rule to Part 4, before Run 2.
2. **Line 503 still leaves out FunctionalTests, and "may" makes it optional.** The new wording
   is "The change may be limited to Ordering.API, Ordering.Domain, Ordering.Infrastructure and
   Ordering.UnitTests". Suggested wording: "The change must be limited to the projects listed in
   Part 2."
3. **`git checkout -- src` throws away EVERY uncommitted change under `src/`.** Right now
   `git status` shows changes only under `training/`: `T1-remove-mediatr.md` is modified and
   `T1-run1-plan.md` is untracked. So the revert is safe today. Before each mutation I check that
   `git status -- src` is empty, and after each revert I check it again.
4. **The Run 2 check for AC3 (line 534) will probably not run as written.** `global.json` sets the
   test runner to `Microsoft.Testing.Platform`. In that mode, I expect `dotnet test` to reject a
   bare project path (`dotnet test tests/Ordering.FunctionalTests`) and require
   `--project tests/Ordering.FunctionalTests`. I have not run it. I will use `--project` and
   report what the plain form does.
5. **The final `git status` will not be "only two new files".** It will also list the two
   `training/` files above, which I do not touch. Neither of them should be picked up by
   accident in your commit.

## Decisions recorded (from the answers)

- Test 1 uses a 2-digit CVV. "Rejected" means 200 OK and no order written.
- The fake-behaviour unit test is replaced by the log-sequence functional test. Run 2 must keep
  the four behaviour log templates exactly as they are.
- All new tests go in `tests/Ordering.FunctionalTests`, following Part 2.
  `Ordering.UnitTests` is not touched.
- The tests read the database directly through `OrderingContext`.
- The OpenAPI snapshot is committed. The `.received.json` file is never committed.
- Mutation check for every test, reported per test, and `src/` is clean at the end.
- Expected total after Run 1: 127 (122 + 5) from `dotnet test --solution eShop.Web.slnf`.
- The dispatch-before-save point (AC6 timing) is left to the diff review.
- What the first run shows about the outbox publish without RabbitMQ gets reported.

## Why the plan changed from v1

- **Expired card -> 2-digit CVV (test 1).** Expiry is also checked by the domain
  (`PaymentMethod.cs:27-30`), so an expired-card test passes with or without the validator. Only
  the validator checks CVV length (`CreateOrderCommandValidator.cs:14`). The domain only checks
  that the CVV is not blank (`PaymentMethod.cs:24`), and the CVV is not stored.
- **Fake-behaviour unit test -> log-sequence functional test (test 2).** A unit test with fakes
  needs MediatR types, so it stops compiling after Run 2 and fails the AC1 grep. It also tests
  MediatR's ordering rather than `Extensions.cs:39-41`.
- **"Rejected" = 200 OK and no order.** `OrdersApi.cs:157-166` returns 200 whatever happens, and
  `IdentifiedCommandHandler.cs:99-102` swallows the validation exception.

## The tests

All five tests go in ONE new file, `tests/Ordering.FunctionalTests/OrderingBehaviourTests.cs`,
class `OrderingBehaviourTests : IClassFixture<OrderingApiFixture>`, written in xUnit v3 with the
versioned client setup from `OrderingApiTests.cs:20-23`. It is a separate class, so it gets its
own fixture instance and database, and no existing file is edited.

Shared private helpers:
- `BuildOrder(string userId, string cvv = "123")` builds a fully valid `CreateOrderRequest`:
  every address field filled, card `4012888888881881`, expiry one year ahead, `CardTypeId = 1`
  (seeded, `OrderingContextSeed.cs:21`), and one basket item.
- `PostOrderAsync(request, Guid requestId)` posts to `api/orders` with `x-requestid`.
- `FindBuyerAndOrdersAsync(string userId)` opens a scope on `fixture.Services`, resolves
  `OrderingContext`, and loads the Buyer by `IdentityGuid` (with `PaymentMethods`) and the Orders
  whose `BuyerId` matches that Buyer.

### Test 1 - `CreateOrder_WithInvalidCvv_ReturnsOkButCreatesNoOrder`
1. File: `OrderingBehaviourTests.cs`.
2. Proves AC4 (validation still runs, on the inner command).
3. Sends a valid order with CVV `"12"`. Asserts 200 OK, and that no Buyer and no Order exist for
   that user id.
4. Why it passes today: the inner command goes back through the pipeline at
   `IdentifiedCommandHandler.cs:87`, where `CreateOrderCommandValidator` throws on the CVV length.
   Lines 99-102 swallow the exception and return `false`, the endpoint returns 200, and the
   handler never runs.

### Test 2 - `CreateOrder_RunsLoggingThenValidatorThenTransaction_OnBothPasses`
1. File: `OrderingBehaviourTests.cs`. It replaces the fake-behaviour unit test.
2. Proves AC4 (behaviour order), plus the second pass that Part 4 requires.
3. Builds a client from `fixture.WithWebHostBuilder(...)` with a capturing `ILoggerProvider`,
   then posts one valid order. It keeps only the entries whose `{OriginalFormat}` is one of the
   four behaviour templates, and asserts this exact sequence:
   - Handling `IdentifiedCommand<CreateOrderCommand,Boolean>` (`LoggingBehavior.cs:9`)
   - Validating the identified command (`ValidatorBehavior.cs:18`)
   - Begin transaction for the identified command (`TransactionBehavior.cs:41`)
   - Handling `CreateOrderCommand`
   - Validating `CreateOrderCommand`
   - Commit transaction for the identified command (`TransactionBehavior.cs:45`)
   It also asserts there is no "Begin transaction" for `CreateOrderCommand`.
4. Why it passes today: MediatR runs behaviours in registration order (`Extensions.cs:39-41`),
   the nested send at line 87 repeats the pipeline, and `TransactionBehavior.cs:27-30` skips a
   second transaction.

### Test 3 - `CreateOrder_SameRequestIdTwice_CreatesOneOrder`
1. File: `OrderingBehaviourTests.cs`.
2. Proves AC5 (idempotency).
3. Posts the same valid order twice, one after the other, with the same `x-requestid`. Asserts
   both return 200 and exactly one Order exists for that user id.
4. Why it passes today: on the second post, `ExistAsync` finds the request row
   (`IdentifiedCommandHandler.cs:41-44`) and returns the duplicate result `true`
   (`CreateOrderCommandHandler.cs:67-70`) without calling the handler.

### Test 4 - `CreateOrder_ValidOrder_CreatesBuyerAndLinksItToOrder`
1. File: `OrderingBehaviourTests.cs`.
2. Proves AC6 (domain events dispatched, including the nested dispatch).
3. Posts one valid order. Asserts a Buyer with `IdentityGuid == userId` and one payment method
   exists, and exactly one Order has `BuyerId == buyer.Id` and a non-null `PaymentId`.
4. Why it passes today: `ValidateOrAddBuyer...Handler` creates the Buyer and saves again (lines
   47-48). That nested save dispatches `BuyerAndPaymentMethodVerifiedDomainEvent`, whose handler
   sets `BuyerId` and `PaymentId` (`UpdateOrderWhen...Handler.cs:22`).

### Test 5 - `OpenApiDocument_MatchesSavedSnapshot`
1. File: `OrderingBehaviourTests.cs`, with the snapshot at
   `tests/Ordering.FunctionalTests/Snapshots/ordering-openapi-v1.json`, found through
   `[CallerFilePath]` (no `.csproj` change).
2. Proves AC8 (the API surface is unchanged).
3. GETs `openapi/v1.json`, swaps the `authorizationUrl`/`tokenUrl` values (the Identity port is
   random) for a placeholder, and compares with `JsonNode.DeepEquals`. If the snapshot is missing,
   it writes `ordering-openapi-v1.received.json` and FAILS.
4. Why it passes today: the snapshot is taken from the current code. I read the received file,
   rename it to the snapshot name, and re-run. Renaming removes the received file, so it cannot be
   committed.

## Mutation check (answer 7)

Before each mutation, `git status -- src` must be empty. Each mutation is ONE temporary edit,
followed by a build and a run of only that test. Every test in the new class has to be green
first, before any mutation starts. After each run: `git checkout -- src`, then `git status -- src`
is empty again.

| Test | Temporary edit under src/ | Expected red on |
|------|---------------------------|-----------------|
| 1 | Comment out the CVV rule, `CreateOrderCommandValidator.cs:14` | "no Buyer / no Order" (the order is created) |
| 2 | Swap `Extensions.cs` lines 40 and 41 (Transaction before Validator) | the sequence (Begin transaction comes before Validating) |
| 3 | `IdentifiedCommandHandler.cs:44`: replace `return CreateResultForDuplicateRequest();` with `return await _mediator.Send(message.Command, cancellationToken);` | "exactly one Order" (two are created) |
| 4 | Comment out `orderToUpdate.SetPaymentMethodVerified(...)`, `UpdateOrderWhenBuyerAndPaymentMethodVerifiedDomainEventHandler.cs:22` | "`BuyerId == buyer.Id`, `PaymentId` not null" |
| 5 | `OrdersApi.cs:15`: change the route `"/cardtypes"` to `"/cardtype"` | the snapshot comparison |

Test 3 does not use the simpler mutation "treat every request as new". That edit makes
`CreateRequestForCommandAsync` throw on the duplicate row, so the test would go red on the status
code and never reach the count. The edit above goes red on the count, which is the assertion that
matters. For each test the report gives: the edit, whether the test went red, the failing
assertion message, and the `git status -- src` result after the revert.

## Files touched in Run 1

- New: `tests/Ordering.FunctionalTests/OrderingBehaviourTests.cs`
- New: `tests/Ordering.FunctionalTests/Snapshots/ordering-openapi-v1.json`
- Temporary, reverted: one file under `src/` per mutation.
- Not touched: any existing test, any `.csproj`, `Ordering.UnitTests`, `training/`.

## Verification and report

1. `dotnet build tests/Ordering.FunctionalTests`
2. `dotnet test --project tests/Ordering.FunctionalTests`: all green. The report says what the
   first valid order shows about the outbox publish without RabbitMQ (answer 10).
3. The mutation checks above, one row each in the report.
4. `dotnet test --solution eShop.Web.slnf`: 127 passed, 0 failed.
5. `git status`: the two new test files, plus the two `training/` files that were already there.
   Nothing under `src/`.
6. Stop. No commit.
