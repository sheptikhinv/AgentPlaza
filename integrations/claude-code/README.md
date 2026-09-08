# Agent Plaza Claude Code integration

`reporter.mjs` is a dependency-free Node.js 18+ hook command. It reads Claude Code hook JSON from stdin, derives a safe event, and posts it to Agent Plaza. Raw hook JSON never leaves the process.

## Install

Make the reporter executable for direct use, or invoke it with `node`:

```sh
chmod +x /absolute/path/to/integrations/claude-code/reporter.mjs
export AGENT_PLAZA_URL="https://plaza.example.com"
export AGENT_PLAZA_TOKEN="installation-token"
export AGENT_PLAZA_INSTALLATION_ID="installation-id"
export AGENT_PLAZA_PROJECT="my-project"
```

Merge the hooks from `settings.example.json` into `~/.claude/settings.json`, replacing the reporter path. `AGENT_PLAZA_URL` and `AGENT_PLAZA_TOKEN` are required for delivery. `AGENT_PLAZA_INSTALLATION_ID` is recommended; when absent, an opaque installation ID is generated and persisted. `AGENT_PLAZA_PROJECT` defaults to `claude-code`.

Person identity is not sent because Agent Plaza derives it from the bearer token. Prompts, paths, tool arguments, outputs, error details, tool names, and the raw hook payload are never transmitted. Tool names are reduced locally to one of `filesystem`, `shell`, `web`, `agent`, or `other`.

Installation/session correlation and monotonic per-session sequences are stored atomically in `${XDG_CACHE_HOME:-~/.cache}/agent-plaza/claude-code-state.json`. Override this with `AGENT_PLAZA_STATE_FILE`. The reporter retains at most 100 session records, uses a request timeout (`AGENT_PLAZA_TIMEOUT_MS`, default `4000`), and retries transient failures with backoff.
