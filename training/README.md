# Training: directing a coding agent through an existing codebase

This fork of Microsoft's [eShop](https://github.com/dotnet/eShop) reference application is where
I practise directing an AI coding agent (Claude Code) through a codebase I did not write. The
work is split into tickets, and each ticket is a small change to the code. For each one, I work
out everything the change touches before the agent writes anything. I then direct the agent and
review what it produces.

The tickets are grouped into stages. Stage 1 holds the first four tickets, and its code changes
are on the `training/stage1` branch. This folder records how each change was scoped, so a reader
can see the planning as well as the final code.

## How each ticket is worked

1. Restate the ticket in my own words, and write down what is out of scope.
2. Ask the agent to list every file and component the change touches, without writing code.
3. Check each item it lists against the code, add what it missed, and remove what is wrong.
4. Write the instruction to the agent: what to change, what to leave alone, and which tests
   prove the change.
5. Have the agent produce a plan, review the plan, and then let it implement.
6. Review the whole diff, build, and run the tests.
7. Record in `SCORECARD.md` what the agent found, missed and got wrong, and what I missed.

## Files

| File                          | Contents                                                                                                      |
|-------------------------------|---------------------------------------------------------------------------------------------------------------|
| `stage1/T1-remove-mediatr.md` | Ticket 1: the requirement, my restatement and the impact matrix (every file and component the change touches) |
| `SCORECARD.md`                | One row per ticket: what the agent found, missed and got wrong, and what I missed                             |

## Baseline before any change

`dotnet test --solution eShop.Web.slnf` ran 122 tests, and all 122 passed. The application ran
under Aspire, and an order was placed from start to finish in the web app.
