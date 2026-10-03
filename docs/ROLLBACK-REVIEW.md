# Review checkpoints and rollback

Original repository baseline: `c6d6e0bf2c260acb5728385e8a0ac47efdd839be` (1.2.149).
Review branch: `feat/verified-panel-reliability-20261003`.

Changes are published only to this separate branch, as numbered, ordinary commits.
No force-push, history rewrite, tag deletion, merge, deployment or release is part of this review.
The original baseline and the binary-capable patch are retained in the review package.

## Inspect the original source safely

From a clone with this commit available, use a separate directory:

```sh
git worktree add --detach ../cehoproxy-original c6d6e0bf2c260acb5728385e8a0ac47efdd839be
```

This leaves the current working directory and its uncommitted edits intact. Do not use
`reset --hard` or overwrite a working tree containing user changes.

## Undo a review change without deleting history

After reviewing the exact commit and saving any local edits, create a new rollback branch
and use `git revert <review-commit>` for the chosen commit, newest first when reverting a sequence.
Review conflicts and rerun the safe tests before publishing that rollback. Reverting source
does not itself install software or change the running VPN.

## Running application and settings

Changing an installed version, restoring routing settings or changing firewall/DNS/VPN state
is a separate operation requiring an explicit, platform-specific plan. Do not delete the
private configuration directory or remove fail-closed rules merely to undo this UI review.
The application's verified-config recovery and updater rollback are separate existing mechanisms;
neither is automatically invoked by a source rollback.
