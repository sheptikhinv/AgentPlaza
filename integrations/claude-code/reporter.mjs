#!/usr/bin/env node

import { createHash, randomUUID } from "node:crypto"
import { mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs"
import { homedir } from "node:os"
import { dirname, join } from "node:path"

const endpoint = process.env.AGENT_PLAZA_URL?.replace(/\/+$/, "")
const token = process.env.AGENT_PLAZA_TOKEN
const project = process.env.AGENT_PLAZA_PROJECT || "claude-code"
const timeoutMs = positiveInteger(process.env.AGENT_PLAZA_TIMEOUT_MS, 4_000)
const stateFile = process.env.AGENT_PLAZA_STATE_FILE || join(
  process.env.XDG_CACHE_HOME || join(homedir(), ".cache"),
  "agent-plaza",
  "claude-code-state.json",
)

function positiveInteger(value, fallback) {
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback
}

function safeConfiguredId(value, name) {
  if (value === undefined || value === "") return undefined
  if (!/^[A-Za-z0-9._:-]{1,128}$/.test(value)) throw new Error(`${name} is invalid`)
  return value
}

function readInput() {
  const chunks = []
  let size = 0
  return new Promise((resolve, reject) => {
    process.stdin.on("data", (chunk) => {
      size += chunk.length
      if (size > 1024 * 1024) {
        reject(new Error("Hook input exceeds 1 MiB"))
        process.stdin.destroy()
        return
      }
      chunks.push(chunk)
    })
    process.stdin.on("end", () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")))
      } catch {
        reject(new Error("Hook input is not valid JSON"))
      }
    })
    process.stdin.on("error", reject)
  })
}

function loadState() {
  try {
    const state = JSON.parse(readFileSync(stateFile, "utf8"))
    return state && typeof state === "object" ? state : {}
  } catch {
    return {}
  }
}

function acquireLock() {
  const lock = `${stateFile}.lock`
  mkdirSync(dirname(stateFile), { recursive: true, mode: 0o700 })
  for (let attempt = 0; attempt < 50; attempt += 1) {
    try {
      mkdirSync(lock, { mode: 0o700 })
      return () => rmSync(lock, { recursive: true, force: true })
    } catch (error) {
      if (error?.code !== "EEXIST") throw error
      if (attempt === 49) {
        rmSync(lock, { recursive: true, force: true })
        mkdirSync(lock, { mode: 0o700 })
        return () => rmSync(lock, { recursive: true, force: true })
      }
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 20)
    }
  }
}

function saveState(state) {
  const temporary = `${stateFile}.${process.pid}.tmp`
  writeFileSync(temporary, `${JSON.stringify(state)}\n`, { mode: 0o600 })
  renameSync(temporary, stateFile)
}

function nextCorrelation(input) {
  const release = acquireLock()
  try {
    const state = loadState()
    state.installationId = safeConfiguredId(process.env.AGENT_PLAZA_INSTALLATION_ID, "AGENT_PLAZA_INSTALLATION_ID")
      || state.installationId
      || randomUUID()
    state.sessions ||= {}

    // Inputs are only used to derive a local opaque key; none of these values are transmitted.
    const localKey = createHash("sha256").update(JSON.stringify([
      typeof input.session_id === "string" ? input.session_id : "",
      typeof input.transcript_path === "string" ? input.transcript_path : "",
      typeof input.cwd === "string" ? input.cwd : "",
    ])).digest("hex")
    const current = state.sessions[localKey] || { id: randomUUID(), sequence: 0 }
    current.sequence += 1
    current.updatedAt = Date.now()
    state.sessions[localKey] = current

    const entries = Object.entries(state.sessions)
      .sort((left, right) => (right[1].updatedAt || 0) - (left[1].updatedAt || 0))
      .slice(0, 100)
    state.sessions = Object.fromEntries(entries)
    saveState(state)
    return { installationId: state.installationId, sessionId: current.id, sequence: current.sequence }
  } finally {
    release?.()
  }
}

function toolKind(name) {
  const normalized = typeof name === "string" ? name.toLowerCase() : ""
  if (["read", "write", "edit", "multiedit", "glob", "grep", "notebookedit"].includes(normalized)) return "filesystem"
  if (["bash", "shell"].includes(normalized)) return "shell"
  if (["webfetch", "websearch"].includes(normalized)) return "web"
  if (["task", "agent"].includes(normalized)) return "agent"
  return "other"
}

function classify(input) {
  const hook = typeof input.hook_event_name === "string" ? input.hook_event_name : "Unknown"
  const kind = toolKind(input.tool_name)
  const mappings = {
    SessionStart: ["session.started", "session", "Claude Code session started."],
    UserPromptSubmit: ["chat.message", "message", "A chat message was submitted."],
    PreToolUse: ["tool.before", kind, `A ${kind} tool execution started.`],
    PostToolUse: ["tool.after", kind, `A ${kind} tool execution completed.`],
    PostToolUseFailure: ["tool.error", kind, `A ${kind} tool execution failed.`],
    Stop: ["session.status", "idle", "Claude Code session became idle."],
    SessionEnd: ["session.ended", "session", "Claude Code session ended."],
    Notification: ["session.notification", "notification", "Claude Code emitted a notification."],
  }
  return mappings[hook] || ["session.activity", "activity", "Claude Code session activity occurred."]
}

async function postWithRetry(event) {
  if (!endpoint || !token) return
  for (let attempt = 0; attempt < 3; attempt += 1) {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), timeoutMs)
    try {
      const response = await fetch(`${endpoint}/v1/events`, {
        method: "POST",
        headers: { authorization: `Bearer ${token}`, "content-type": "application/json" },
        body: JSON.stringify(event),
        signal: controller.signal,
      })
      if (response.ok || (response.status < 500 && response.status !== 429)) return
    } catch {
      // Hooks are best-effort and should not interfere with Claude Code.
    } finally {
      clearTimeout(timer)
    }
    await new Promise((resolve) => setTimeout(resolve, 200 * 2 ** attempt))
  }
}

try {
  const input = await readInput()
  const correlation = nextCorrelation(input)
  const [type, kind, summary] = classify(input)
  await postWithRetry({
    schemaVersion: 1,
    eventId: randomUUID(),
    ...correlation,
    source: "claude-code",
    type,
    occurredAt: new Date().toISOString(),
    payload: { kind, summary, project },
  })
} catch (error) {
  process.stderr.write(`agent-plaza reporter: ${error instanceof Error ? error.message : "unknown error"}\n`)
  process.exitCode = 1
}
