## Issue Tracker

Issues, specs, and pull requests live in GitHub and are managed through the GitHub MCP server, which authenticates as this agent's dedicated bot account. Never use `gh` or `glab`: those act as the human logged in on this machine. If the MCP tools are unavailable, stop and say so instead of falling back.

- Pass `owner` and `repo` explicitly, resolved from `git remote -v`.
- `issue_write` creates, updates, closes, and labels issues; `issue_read` reads one; `list_issues` and `search_issues` find them.
- `add_issue_comment` comments. `sub_issue_write` attaches a ticket to its parent; `sub_issue_id` is the database id, not the issue number.
- Blocking edges live in the body under `## Blocked by`, because the dependency tools are feature-gated.
- `create_pull_request`, `pull_request_read`, and `update_pull_request` handle pull requests.

## Triage Labels

Default five canonical roles (needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix). See `docs/agents/triage-labels.md`.

## Domain Docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
