import { useEffect, useState } from 'react'
import type { CSSProperties, FormEvent } from 'react'
import './App.css'

type PresenceStatus = 'working' | 'waiting' | 'error' | 'idle' | 'offline' | string

type PlazaPerson = {
  id: string
  name: string
  avatarSeed: string
  status: PresenceStatus
  activityKind: string | null
  summary: string | null
  project: string | null
  lastSeenAt: string | null
  activeSessionCount: number
  revision: number
}

type PlazaSnapshot = {
  people: PlazaPerson[]
}

type ViewState = 'loading' | 'ready' | 'login' | 'error'
type StreamState = 'connecting' | 'live' | 'reconnecting'

const API_URL = '/v1/plaza'

function hashSeed(seed: string) {
  return [...seed].reduce((hash, character) => {
    return (hash * 31 + character.charCodeAt(0)) >>> 0
  }, 2166136261)
}

function timeAgo(value: string | null) {
  if (!value) return 'No recent signal'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Recently seen'

  const elapsed = Math.max(0, Date.now() - date.getTime())
  const minutes = Math.floor(elapsed / 60_000)
  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

function labelActivity(kind: string | null) {
  if (!kind) return 'Available'
  return kind.replaceAll(/[-_]/g, ' ').replace(/^./, (letter) => letter.toUpperCase())
}

function isPresent(status: PresenceStatus) {
  return status.toLowerCase() !== 'offline'
}

function Avatar({ person, size = 'regular' }: { person: PlazaPerson; size?: 'small' | 'regular' }) {
  const seed = hashSeed(person.avatarSeed)
  const hue = 202 + (seed % 72)
  const hair = 218 + (seed % 35)
  const variant = seed % 3

  return (
    <span
      className={`avatar avatar--${size}`}
      style={{
        '--avatar-bg': `hsl(${hue} 68% 84%)`,
        '--avatar-shirt': `hsl(${(hue + 42) % 360} 58% 58%)`,
        '--avatar-hair': `hsl(${hair} 30% 26%)`,
      } as CSSProperties}
      aria-hidden="true"
    >
      <svg viewBox="0 0 64 64">
        <circle cx="32" cy="32" r="31" fill="var(--avatar-bg)" />
        <path d="M12 64c1-14 8-21 20-21s19 7 20 21" fill="var(--avatar-shirt)" />
        <path d="M23 39h18v11c-5 4-13 4-18 0z" fill="#dca98e" />
        <ellipse cx="32" cy="29" rx="15" ry="17" fill="#efc1a4" />
        {variant === 0 && <path d="M17 29c0-15 8-20 17-20 8 0 14 4 15 13-8 1-15-2-21-7-1 6-5 10-11 14z" fill="var(--avatar-hair)" />}
        {variant === 1 && <path d="M17 27c0-13 7-19 15-19 11 0 17 8 16 22l-4-8c-9 2-16-1-21-6z" fill="var(--avatar-hair)" />}
        {variant === 2 && <path d="M17 27c1-12 6-18 16-18 9 0 15 6 16 18-4-5-9-8-16-8-6 0-12 3-16 8z" fill="var(--avatar-hair)" />}
        <circle cx="26.5" cy="30" r="1.4" fill="#26314f" />
        <circle cx="37.5" cy="30" r="1.4" fill="#26314f" />
        <path d="M28 36c2 2 6 2 8 0" fill="none" stroke="#a96161" strokeLinecap="round" strokeWidth="1.5" />
      </svg>
      <span className={`presence-dot presence-dot--${person.status.toLowerCase()}`} />
    </span>
  )
}

function StatusPill({ status }: { status: PresenceStatus }) {
  return <span className={`status-pill status-pill--${status.toLowerCase()}`}>{status}</span>
}

function PersonDesk({ person, index }: { person: PlazaPerson; index: number }) {
  const isOffline = person.status.toLowerCase() === 'offline'
  return (
    <article className={`desk-station ${isOffline ? 'desk-station--quiet' : ''}`} style={{ '--desk-delay': `${index * 55}ms` } as CSSProperties}>
      <div className="desk-station__person">
        <Avatar person={person} />
        <div>
          <h3>{person.name}</h3>
          <p>{person.project || 'Open desk'}</p>
        </div>
      </div>
      <div className="desk" aria-hidden="true">
        <span className="desk__plant"><i /><i /><i /></span>
        <span className="desk__screen"><i /></span>
        <span className="desk__mug" />
        <span className="desk__edge" />
      </div>
      <div className="desk-station__work">
        <span>{labelActivity(person.activityKind)}</span>
        {person.activeSessionCount > 0 && <b>{person.activeSessionCount} {person.activeSessionCount === 1 ? 'session' : 'sessions'}</b>}
      </div>
      <p className="desk-station__summary">{person.summary || (isOffline ? `Last seen ${timeAgo(person.lastSeenAt)}` : 'Ready for the next task')}</p>
    </article>
  )
}

function LoginView({ onLogin }: { onLogin: (token: string) => Promise<void> }) {
  const [token, setToken] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setError('')
    try {
      await onLogin(token)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not sign in with that token.')
      setSubmitting(false)
    }
  }

  return (
    <main className="gate">
      <div className="gate__window" aria-hidden="true"><span /><span /><span /></div>
      <section className="gate__panel" aria-labelledby="login-title">
        <div className="brand-mark"><span>AP</span></div>
        <p className="eyebrow">Agent Plaza / private floor</p>
        <h1 id="login-title">Come into the studio.</h1>
        <p className="gate__intro">Use your access token to see who is here and what the team is shaping right now.</p>
        <form onSubmit={submit}>
          <label htmlFor="token">Access token</label>
          <div className="token-field">
            <input id="token" name="token" type="password" autoComplete="current-password" value={token} onChange={(event) => setToken(event.target.value)} required autoFocus />
            <button type="submit" disabled={submitting}>{submitting ? 'Opening…' : 'Enter plaza'}<span aria-hidden="true">→</span></button>
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
        </form>
        <p className="gate__note"><span /> Your token stays in the secure session cookie.</p>
      </section>
    </main>
  )
}

function LoadingView() {
  return (
    <main className="center-state" aria-live="polite">
      <div className="loading-room" aria-hidden="true"><span /><span /><span /></div>
      <p className="eyebrow">Opening the studio</p>
      <h1>Gathering the team…</h1>
    </main>
  )
}

function App() {
  const [view, setView] = useState<ViewState>('loading')
  const [people, setPeople] = useState<PlazaPerson[]>([])
  const [error, setError] = useState('')
  const [stream, setStream] = useState<StreamState>('connecting')
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    let source: EventSource | undefined

    async function load() {
      setView('loading')
      setError('')
      try {
        const response = await fetch(API_URL, { credentials: 'include', signal: controller.signal })
        if (response.status === 401) {
          setView('login')
          return
        }
        if (!response.ok) throw new Error(`The plaza returned ${response.status}.`)
        const snapshot = await response.json() as PlazaSnapshot
        setPeople(snapshot.people)
        setView('ready')

        source = new EventSource(`${API_URL}/stream`)
        source.onopen = () => setStream('live')
        source.onerror = () => setStream('reconnecting')
        source.addEventListener('plaza.snapshot', (event) => {
          const next = JSON.parse((event as MessageEvent<string>).data) as PlazaSnapshot
          setPeople(next.people)
        })
        source.addEventListener('presence.updated', (event) => {
          const person = JSON.parse((event as MessageEvent<string>).data) as PlazaPerson
          setPeople((current) => {
            const existing = current.find((entry) => entry.id === person.id)
            if (existing && existing.revision > person.revision) return current
            return existing ? current.map((entry) => entry.id === person.id ? person : entry) : [...current, person]
          })
        })
      } catch (reason) {
        if (controller.signal.aborted) return
        setError(reason instanceof Error ? reason.message : 'The studio could not be reached.')
        setView('error')
      }
    }

    void load()
    return () => {
      controller.abort()
      source?.close()
    }
  }, [reloadKey])

  async function login(token: string) {
    const response = await fetch('/v1/auth/token', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    })
    if (!response.ok) {
      throw new Error(response.status === 401 ? 'That token was not accepted.' : `Sign-in failed (${response.status}).`)
    }
    setReloadKey((key) => key + 1)
  }

  if (view === 'login') return <LoginView onLogin={login} />
  if (view === 'loading') return <LoadingView />
  if (view === 'error') {
    return (
      <main className="center-state center-state--error">
        <div className="error-signal" aria-hidden="true">!</div>
        <p className="eyebrow">Studio unavailable</p>
        <h1>We lost the floor plan.</h1>
        <p>{error}</p>
        <button className="primary-button" onClick={() => setReloadKey((key) => key + 1)}>Try again</button>
      </main>
    )
  }

  const onlineCount = people.filter((person) => isPresent(person.status)).length
  const activeCount = people.filter((person) => person.activeSessionCount > 0).length

  return (
    <div className="app-shell">
      <header className="topbar">
        <a className="wordmark" href="#studio" aria-label="Agent Plaza home"><span className="brand-mark brand-mark--small">AP</span><strong>Agent Plaza</strong></a>
        <p className="topbar__location"><span aria-hidden="true">⌖</span> Team studio · live floor</p>
        <div className={`connection connection--${stream}`} role="status"><span />{stream === 'live' ? 'Live' : stream === 'reconnecting' ? 'Reconnecting' : 'Connecting'}</div>
      </header>

      <main className="plaza" id="studio">
        <aside className="roster" aria-labelledby="roster-title">
          <div className="roster__heading">
            <div><p className="eyebrow">Today’s floor</p><h2 id="roster-title">The crew</h2></div>
            <span>{people.length}</span>
          </div>
          <div className="roster__list">
            {people.map((person) => (
              <div className="roster-person" key={person.id}>
                <Avatar person={person} size="small" />
                <div><strong>{person.name}</strong><span>{person.project || labelActivity(person.activityKind)}</span></div>
                <StatusPill status={person.status} />
              </div>
            ))}
          </div>
           <div className="roster__legend"><span><i className="dot dot--online" />{onlineCount} present</span><span><i className="dot dot--away" />{people.length - onlineCount} offline</span></div>
        </aside>

        <section className="studio" aria-labelledby="studio-title">
          <div className="studio__header">
            <div><p className="eyebrow">Workspace / {new Intl.DateTimeFormat('en', { weekday: 'long' }).format(new Date())}</p><h1 id="studio-title">What’s moving</h1></div>
            <div className="studio__stats"><span><b>{onlineCount}</b> in the plaza</span><span><b>{activeCount}</b> building now</span></div>
          </div>

          {people.length === 0 ? (
            <div className="empty-floor"><div className="empty-floor__chair" aria-hidden="true" /><h2>The studio is quiet.</h2><p>No one has checked in yet. This floor will update as the team arrives.</p></div>
          ) : (
            <div className="floor">
              <div className="floor__window" aria-hidden="true"><i /><i /><i /></div>
              <div className="floor__rug" aria-hidden="true"><span>PLAZA</span></div>
              <div className="floor__desks">{people.map((person, index) => <PersonDesk key={person.id} person={person} index={index} />)}</div>
            </div>
          )}
        </section>

        <aside className="pulse" aria-labelledby="pulse-title">
          <p className="eyebrow">Signals</p>
          <h2 id="pulse-title">Around the room</h2>
          <div className="pulse__line" />
          {people.slice().sort((a, b) => b.revision - a.revision).slice(0, 5).map((person) => (
            <article className="pulse-item" key={person.id}>
              <span className={`pulse-item__mark pulse-item__mark--${person.status.toLowerCase()}`} />
              <div><strong>{person.name}</strong><p>{person.summary || labelActivity(person.activityKind)}</p><time dateTime={person.lastSeenAt || undefined}>{timeAgo(person.lastSeenAt)}</time></div>
            </article>
          ))}
          <div className="pulse__footer"><span>Auto-refreshing</span><i /></div>
        </aside>
      </main>
    </div>
  )
}

export default App
