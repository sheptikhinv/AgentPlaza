# Agent Plaza OpenCode integration

A dependency-free TypeScript plugin that reports sanitized OpenCode lifecycle events to Agent Plaza. The `@opencode-ai/plugin` import is type-only and is removed at transpile time; no package is required by the plugin at runtime.

## Install

Reference `index.ts` from OpenCode's `opencode.json`, as shown in `opencode.example.json`, using an absolute `file://` URL. Alternatively, copy or symlink `index.ts` into an OpenCode plugin directory such as `.opencode/plugins/agent-plaza.ts`.

Set these variables in the environment that starts OpenCode:

```sh
export AGENT_PLAZA_URL="https://plaza.example.com"
export AGENT_PLAZA_TOKEN="installation-token"
export AGENT_PLAZA_INSTALLATION_ID="installation-id"
export AGENT_PLAZA_PROJECT="my-project"
```

`AGENT_PLAZA_URL`, `AGENT_PLAZA_TOKEN`, and `AGENT_PLAZA_INSTALLATION_ID` are required. `AGENT_PLAZA_PROJECT` is a non-sensitive alias and defaults to `opencode`. Person identity is intentionally not configured or transmitted: Agent Plaza derives it from the installation token.

Optional controls are `AGENT_PLAZA_QUEUE_LIMIT` (default `256`), `AGENT_PLAZA_TIMEOUT_MS` (default `5000`), and `AGENT_PLAZA_HEARTBEAT_MS` (default `30000`). Delivery is serialized, bounded, retried with backoff, and best-effort so telemetry cannot break an OpenCode session.

The plugin reports `session.status`, `chat.message`, tool before/after, `session.error`, and heartbeats. It only reads session/status identifiers needed for correlation. Prompts, filesystem paths, tool names, arguments, outputs, error details, and raw event objects are never added to requests.
