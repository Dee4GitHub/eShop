# Scorecard

One row per ticket, filled in when the ticket is finished.

| Ticket                 | Found | Missed | Wrong | I missed | Out of scope | Green first time | Minutes |
|------------------------|-------|--------|-------|----------|--------------|------------------|---------|
| T1 Remove MediatR      |       |        |       |          |              |                  |         |
| T2 Cancellation reason |       |        |       |          |              |                  |         |
| T3 v2 order details    |       |        |       |          |              |                  |         |
| T4 Maximum order value |       |        |       |          |              |                  |         |

## What each column means

| Column           | Meaning                                                                     |
|------------------|-----------------------------------------------------------------------------|
| Found            | Rows of the impact matrix the agent listed correctly                        |
| Missed           | Rows the agent did not list, which I found when checking the code           |
| Wrong            | Rows the agent listed that were wrong or out of scope                       |
| I missed         | Rows I missed, found by comparing with an independent list after the ticket |
| Out of scope     | Files changed in the final diff that the ticket did not need                |
| Green first time | Whether the build and all tests passed on the agent's first implementation  |
| Minutes          | Time from reading the ticket to the final commit                            |
