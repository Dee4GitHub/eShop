
# T1 Run 1: prompts and report

## Session

- Claude Code, Opus 5.5, new session, plan mode, then auto mode for the run.

## Message 1: the Run 1 prompt

Read `training/stage1/T1-remove-mediatr.md`. It has the ticket, the acceptance criteria, the verified impact matrix, and my instruction under "## Step 4: instruction to the agent".

Do Run 1 only, as described in Part 5 of that instruction. Run 1 adds tests and changes no code under `src/`. Do not plan Run 2.

In your plan, for each new test give:
1. The file it goes in, and the test name.
2. The acceptance criterion it proves.
3. What it sends and what it asserts, in one or two lines.
4. Why it passes on the current MediatR code.

If a test cannot be written the way Part 5 describes, say so and say why. Do not change the test to something weaker without telling me.

List anything in the instruction that is unclear or that you think is wrong, before the plan.

Put the full plan in your reply as well as in the plan file.

The agent's plan, with its 10 questions, is in `T1-run1-plan.md` (first version, replaced by the second).

## Message 2: my answers to its 10 items

Answers to your items:

1. Agreed. Use a 2-digit CVV, not an expired card.
2. Agreed. "Rejected" means 200 OK and no order written.
3. Agreed. Replace the fake-behaviour unit test with your log-sequence functional test. Run 2 must keep the four behaviour log templates exactly as they are.
4. Follow Part 2. The Part 3 line is out of date and I will fix it.
5. Yes, a direct database read through `OrderingContext` is fine.
6. Yes, the snapshot file is hand-kept test data and may be committed. Never commit the `.received.json` file.
7. Yes, do the mutation check. For each test, make one temporary edit under `src/`, confirm the test goes red, then revert with `git checkout -- src`. Report the edit and the result for each test. After the last revert, `git status` must show no change under `src/`.
8. The baseline 122 was from `dotnet test --solution eShop.Web.slnf`, which includes `Ordering.FunctionalTests`. Expected after Run 1: 127.
9. Agreed. The dispatch-before-save point is left to the diff review.
10. Noted. Report what the first run shows.

Do not start Run 1 yet. Update the plan with these answers. If my answers raise anything new that is unclear or wrong, list it at the top of the updated plan. Then stop and wait for me to approve.

The updated plan (second version) is in `T1-run1-plan.md`. It raised 5 new items. I fixed the instruction file and committed before approving.

## Message 3: the go-ahead

I have updated the instruction file with your items 1 and 2, and committed. Items 3 to 5 are fine. Go ahead with Run 1.

## Message 4: the agent's question during the run, and my choice

The agent asked: "A valid order returns 500 in the test fixture because the outbox can't find the event types (the test runner is the entry assembly). The order itself is committed. How should tests 2, 3 and 4 handle the HTTP status?"

Options it gave: drop the status check, assert 500, try SetEntryAssembly, or stop.

My choice: drop the status check. Tests 2, 3 and 4 assert the database and log outcomes only. Asserting 500 would lock in a bug that only exists in tests. SetEntryAssembly changes a setting that the existing tests share.

## My check after the run

`dotnet test --solution eShop.Web.slnf`: 127 passed.

## The agent's final report

Run 1 is done: dotnet test --solution eShop.Web.slnf gives 127 passed, 0 failed (122 + 5 new), and nothing under src/ is changed. I have not committed anything.

Files to review and commit:
- tests/Ordering.FunctionalTests/OrderingBehaviourTests.cs, which holds all five tests.
- tests/Ordering.FunctionalTests/Snapshots/ordering-openapi-v1.json. Before accepting it I checked it has all 6 routes, version 1.0, and both Identity URLs masked.
- No .received.json file is left behind.

Mutation checks. Each was one temporary edit, followed by git checkout -- src and a clean git status -- src.

┌──────────────┬──────────────────────────────────────────────────────┬──────────────────────────────────────────┐
│     Test     │                    Temporary edit                    │                  Result                  │
├──────────────┼──────────────────────────────────────────────────────┼──────────────────────────────────────────┤
│ 1, CVV       │ Commented out the CVV rule,                          │ Red: Assert.Null() Failure, a buyer was  │
│              │ CreateOrderCommandValidator.cs:14                    │ written                                  │
├──────────────┼──────────────────────────────────────────────────────┼──────────────────────────────────────────┤
│              │                                                      │ Red: sequence differs at position 1,     │
│ 2, log order │ Swapped Extensions.cs lines 40 and 41                │ "Begin transaction" came before          │
│              │                                                      │ "Validating"                             │
├──────────────┼──────────────────────────────────────────────────────┼──────────────────────────────────────────┤
│ 3,           │ IdentifiedCommandHandler.cs:44 sends the command     │ Red: Assert.Single() Failure, 2 orders   │
│ idempotency  │ instead of returning the duplicate result            │                                          │
├──────────────┼──────────────────────────────────────────────────────┼──────────────────────────────────────────┤
│ 4, domain    │ Commented out SetPaymentMethodVerified,              │ Red: Assert.Single() Failure, empty.     │
│ events       │ UpdateOrderWhen...Handler.cs:22                      │ Orders are looked up by BuyerId, which   │
│              │                                                      │ stayed null                              │
├──────────────┼──────────────────────────────────────────────────────┼──────────────────────────────────────────┤
│ 5, OpenAPI   │ Changed OrdersApi.cs:15 from "/cardtypes" to         │ Red: "The OpenAPI document differs from  │
│              │ "/cardtype"                                          │ the saved snapshot"                      │
└──────────────┴──────────────────────────────────────────────────────┴──────────────────────────────────────────┘

Changes from the approved plan:
- Status checks dropped for valid orders (your decision). Tests 2, 3 and 4 no longer check for 200 on a valid order, and a comment above the class explains why. Test 1 keeps its 200 check. Test 3 checks for 200 on the duplicate post, and that passes because the duplicate writes no integration event.
- Assertions reordered in tests 1 and 3. In the first mutation runs, both tests went red on the status check rather than the database check. With the validator off (test 1), or with the duplicate re-run (test 3), an order gets created and then hits the fixture's 500. So the database checks now come first, and each mutation fails on the check it was meant to prove, as in the table. No assertion was removed in this step.
- Test 5 bug fixed. The Snapshots folder didn't exist yet, so writing the received file failed; the test now creates the folder.

Your item 10: what the first run showed. A valid order commits, and then the endpoint returns 500.
- IntegrationEventLogService.cs:13-16 finds event types by scanning Assembly.GetEntryAssembly(). Under the test runner, that's Ordering.FunctionalTests, which contains no event types.
- The lookup returns null, and the exception is thrown at OrderingIntegrationEventService.cs:17, outside its try/catch. So I was wrong that the outbox publish "fails fast and is caught".
- This only happens in the test fixture, and it's outside T1's scope.

Other things to know:
- My item 4 was wrong: the plain dotnet test tests/Ordering.FunctionalTests works under Microsoft.Testing.Platform.
- For Run 2: the full solution run rebuilds Catalog.API, which rewrites src/Catalog.API/Catalog.API.json and Catalog.API_v2.json with LF line endings. Only the line endings change, not the content. I restored both files, but Run 2 will see the same thing and must not commit them.
- training/stage1/T1-remove-mediatr.md shows as modified. I didn't edit it, so I assume it's your change since the commit; please check it before you commit.