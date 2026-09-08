import type { Plugin } from "@opencode-ai/plugin"

type JsonObject = Record<string, unknown>

interface PlazaEvent {
  schemaVersion: 1
  eventId: string
  installationId: string
  sessionId: string
  sequence: number
  source: "opencode"
  type: string
  occurredAt: string
  payload: {
    kind: string
    summary: string
    project: string
  }
}

const env = (globalThis as typeof globalThis & {
  process?: { env?: Record<string, string | undefined> }
}).process?.env ?? {}

const endpoint = env.AGENT_PLAZA_URL?.replace(/\/+$/, "")
const token = env.AGENT_PLAZA_TOKEN
const installationId = env.AGENT_PLAZA_INSTALLATION_ID
const project = env.AGENT_PLAZA_PROJECT || "opencode"
const queueLimit = positiveInteger(env.AGENT_PLAZA_QUEUE_LIMIT, 256)
const timeoutMs = positiveInteger(env.AGENT_PLAZA_TIMEOUT_MS, 5_000)
const heartbeatMs = positiveInteger(env.AGENT_PLAZA_HEARTBEAT_MS, 30_000)

function positiveInteger(value: string | undefined, fallback: number): number {
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback
}

function record(value: unknown): JsonObject | undefined {
  return value !== null && typeof value === "object" ? value as JsonObject : undefined
}

function sessionIdFrom(value: unknown): string | undefined {
  const item = record(value)
  if (!item) return undefined

  for (const key of ["sessionID", "sessionId", "session_id"]) {
    const candidate = item[key]
    if (typeof candidate === "string" && candidate.length > 0 && candidate.length <= 256) {
      return candidate
    }
  }

  return sessionIdFrom(item.properties) || sessionIdFrom(item.info)
}

function statusFrom(value: unknown): "busy" | "active" | "idle" | "retry" | "unknown" {
  const properties = record(record(value)?.properties)
  const status = record(properties?.status)
  const candidate = status?.type ?? properties?.status
  return candidate === "busy" || candidate === "active" || candidate === "idle" || candidate === "retry"
    ? candidate
    : "unknown"
}

function classifyTool(value: unknown): { kind: string; started: string; completed: string } {
  const tool = String(record(value)?.tool ?? "").toLowerCase()
  if (["read", "glob", "grep", "list", "webfetch", "websearch"].includes(tool)) {
    return { kind: "reading", started: "Exploring project context.", completed: "Reviewed project context." }
  }
  if (["edit", "write", "apply_patch"].includes(tool)) {
    return { kind: "editing", started: "Editing code.", completed: "Updated code." }
  }
  if (tool === "task") {
    return { kind: "planning", started: "Coordinating another agent.", completed: "Agent task completed." }
  }
  if (tool === "bash") {
    return { kind: "running", started: "Running a command.", completed: "Command completed." }
  }
  return { kind: "working", started: "Using a development tool.", completed: "Development step completed." }
}

class DeliveryQueue {
  private readonly pending: Array<{ event: PlazaEvent; coalesceKey?: string }> = []
  private readonly coalesced = new Set<string>()
  private running = false

  enqueue(event: PlazaEvent, coalesceKey?: string): void {
    if (coalesceKey && this.coalesced.has(coalesceKey)) return
    if (this.pending.length >= queueLimit) {
      const dropped = this.pending.shift()
      if (dropped?.coalesceKey) this.coalesced.delete(dropped.coalesceKey)
    }
    this.pending.push({ event, coalesceKey })
    if (coalesceKey) this.coalesced.add(coalesceKey)
    void this.drain()
  }

  private async drain(): Promise<void> {
    if (this.running) return
    this.running = true
    try {
      while (this.pending.length > 0) {
        const item = this.pending.shift()!
        if (item.coalesceKey) this.coalesced.delete(item.coalesceKey)
        await postWithRetry(item.event)
      }
    } finally {
      this.running = false
      if (this.pending.length > 0) void this.drain()
    }
  }
}

async function postWithRetry(event: PlazaEvent): Promise<void> {
  if (!endpoint || !token) return

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), timeoutMs)
    try {
      const response = await fetch(`${endpoint}/v1/events`, {
        method: "POST",
        headers: {
          authorization: `Bearer ${token}`,
          "content-type": "application/json",
        },
        body: JSON.stringify(event),
        signal: controller.signal,
      })
      if (response.ok) return
      if (response.status < 500 && response.status !== 429) return
    } catch {
      // Delivery is best-effort and must never interrupt an OpenCode session.
    } finally {
      clearTimeout(timeout)
    }
    await new Promise((resolve) => setTimeout(resolve, 250 * 2 ** attempt))
  }
}

export const AgentPlazaPlugin: Plugin = async () => {
  const queue = new DeliveryQueue()
  const sequences = new Map<string, number>()
  const activeSessions = new Map<string, number>()

  const emit = (sessionId: string | undefined, type: string, kind: string, summary: string, coalesceKey?: string): void => {
    if (!installationId || !sessionId) return
    const sequence = (sequences.get(sessionId) ?? 0) + 1
    sequences.set(sessionId, sequence)
    activeSessions.set(sessionId, Date.now())
    queue.enqueue({
      schemaVersion: 1,
      eventId: crypto.randomUUID(),
      installationId,
      sessionId,
      sequence,
      source: "opencode",
      type,
      occurredAt: new Date().toISOString(),
      payload: { kind, summary, project },
    }, coalesceKey)
  }

  const heartbeat = setInterval(() => {
    const staleBefore = Date.now() - 30 * 60_000
    for (const [sessionId, lastSeen] of activeSessions) {
      if (lastSeen < staleBefore) {
        activeSessions.delete(sessionId)
        continue
      }
      emit(sessionId, "heartbeat", "heartbeat", "OpenCode session is active.", `heartbeat:${sessionId}`)
    }
  }, heartbeatMs)
  ;(heartbeat as unknown as { unref?: () => void }).unref?.()

  return {
    event: async ({ event }) => {
      const sessionId = sessionIdFrom(event)
      if (event.type === "session.status") {
        const status = statusFrom(event)
        emit(sessionId, "session.status", status, `OpenCode session status: ${status}.`)
      } else if (event.type === "session.error") {
        emit(sessionId, "session.error", "error", "OpenCode session reported an error.")
      }
    },
    "chat.message": async (input) => {
      emit(sessionIdFrom(input), "chat.message", "planning", "Planning the next step.")
    },
    "tool.execute.before": async (input) => {
      const activity = classifyTool(input)
      emit(sessionIdFrom(input), "tool.before", activity.kind, activity.started)
    },
    "tool.execute.after": async (input) => {
      const activity = classifyTool(input)
      emit(sessionIdFrom(input), "tool.after", activity.kind, activity.completed)
    },
  }
}

export default AgentPlazaPlugin
