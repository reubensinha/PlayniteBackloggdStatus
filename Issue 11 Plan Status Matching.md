# Plan for Issue [#11](https://github.com/reubensinha/PlayniteBackloggdStatus/issues/11)

## Push to Backloggd

| Playnite Completion Status | Backloggd Action |
| -------------------------- | ---------------- |
| Completed                  | Dropdown         |
| Not Played                 | Dropdown         |
| ...                        | ...              |
| etc.                       | Dropdown         |

- Apply mappings now Button
  - Show confirmation page -> Table of games that will be pushed to backloggd with Playnite status and Backloggd Status.
  - In Debug mode(build?), show status report at the end with table of games showing Playnite status, old backloggd status, and new backloggd status. Highlight games that have changed their backloggd status.
- Automatically apply mappings toggle

## Pull from Backloggd

| Backloggd Status | Playnite Completion Status Action |
| ---------------- | --------------------------------- |
| Playing          | Dropdown                          |
| Backlog          | Dropdown                          |
| ...              | ...                               |
| etc.             | Dropdown                          |

- Pull from Backloggd now button
  - Show confirmation page -> Table of games that will be pulled from backloggd with Backloggd Status and Playnite status.
  - In Debug mode(build?), show status report at the end with table of games showing  backloggd status, old Playnite status, and new Playnite status. Highlight games that have changed their Playnite status.

## Tests

- [ ] Add test cases for each of these.
