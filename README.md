# Agent Plaza

Agent Plaza is a small live studio for a team of coding agents. One avatar represents one person; OpenCode and Claude Code sessions are aggregated into that person's current presence.

## Run with Docker

```sh
docker compose up --build
```

Open `http://localhost:5080` and use the development viewer token `change-me-viewer`.

The default configuration creates one person with two reporter installations:

| Reporter | Installation ID | Token |
| --- | --- | --- |
| OpenCode | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1` | `change-me-opencode` |
| Claude Code | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2` | `change-me-claude` |

Change all three tokens before exposing the service. The viewer token can be supplied as `AGENT_PLAZA_VIEWER_TOKEN` to Docker Compose. Reporter identities are configured under `Bootstrap:People` in `src/AgentPlaza.Api/appsettings.json`; their tokens are SHA-256 hashed before being persisted.

## Run for development

Start only PostgreSQL:

```sh
docker compose up postgres
```

Run the API:

```sh
dotnet run --project src/AgentPlaza.Api --launch-profile http
```

Run the frontend in another terminal:

```sh
npm install --prefix web
npm run dev --prefix web
```

Vite proxies `/v1` to `http://localhost:5080`. The Docker build places the production frontend in the API's `wwwroot` directory.

## Connect reporters

OpenCode setup is documented in `integrations/opencode/README.md`. Use the OpenCode installation ID and token from the table above.

Claude Code setup is documented in `integrations/claude-code/README.md`. Use the Claude Code installation ID and token from the table above. This works with Claude Code and the Code surface in Claude Desktop; ordinary Desktop Chat does not expose equivalent lifecycle hooks.

The integrations send only an event category, a fixed safe summary, and a configured project alias. Prompts, source code, paths, tool arguments, outputs, and error details stay on the workstation.

## API

- `POST /v1/auth/token` exchanges the shared viewer token for an HttpOnly cookie.
- `GET /v1/plaza` returns the current team snapshot.
- `GET /v1/plaza/stream` streams snapshot and presence updates over SSE.
- `POST /v1/events` accepts authenticated reporter events.
- `GET /health/live` and `GET /health/ready` expose health state.

Sessions expire after 90 seconds without a heartbeat or another event. OpenCode sends heartbeats automatically. Claude Code sessions also become offline after inactivity because shutdown hooks cannot be guaranteed.
