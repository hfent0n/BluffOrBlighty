// Smoke test for a deployed revision: run before it gets any traffic.
//
//   node scripts/smoke-test.mjs <base-url> [expected-store]
//
// Checks the health endpoint (which also checks Redis is reachable), that the page is served,
// and plays the start of a real game over WebSockets: one player creates a game and a second
// joins, and the first must see the second. That's the path a broken Redis setup took down.
// Uses the SignalR client from client/node_modules (run `npm ci` in client first).
import { createRequire } from 'node:module'

const [base, expectedStore] = process.argv.slice(2)
if (!base) {
  console.error('usage: node scripts/smoke-test.mjs <base-url> [expected-store]')
  process.exit(2)
}
const require = createRequire(new URL('../client/package.json', import.meta.url))
const signalR = require('@microsoft/signalr')

const fail = (message) => {
  console.error(`::error::Smoke test failed: ${message}`)
  process.exit(1)
}
const step = (message) => console.log(`✓ ${message}`)
setTimeout(() => fail('timed out after 90 s'), 90_000).unref()

// 1. Health: 200 only if the store (Redis) is actually reachable.
const health = await fetch(`${base}/api/health`).catch((e) => fail(`health request: ${e.message}`))
const healthBody = await health.text()
if (!health.ok) fail(`/api/health returned ${health.status}: ${healthBody}`)
const { store } = JSON.parse(healthBody)
if (expectedStore && store !== expectedStore) fail(`expected store "${expectedStore}", got "${store}"`)
step(`health ok (store: ${store})`)

// 2. The client app is served.
const page = await fetch(base)
if (!page.ok || !(await page.text()).includes('<div id="root">')) fail(`home page returned ${page.status}`)
step('home page served')

// 3. A real game over WebSockets only.
async function player() {
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${base}/hub/game`, { transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true })
    .configureLogging(signalR.LogLevel.None)
    .build()
  const p = { conn, state: null }
  conn.on('state', (s) => (p.state = s))
  try {
    await conn.start()
  } catch (e) {
    fail(`WebSocket connection to the game hub failed: ${e.message}`)
  }
  return p
}
async function until(what, condition) {
  const deadline = Date.now() + 15_000
  while (Date.now() < deadline) {
    if (condition()) return
    await new Promise((r) => setTimeout(r, 100))
  }
  fail(`timed out waiting for: ${what}`)
}

const host = await player()
const guest = await player()
step('two WebSocket connections open')

let joined
try {
  joined = await host.conn.invoke('CreateGame', 'Smoke Test')
  await guest.conn.invoke('JoinGame', joined.code, 'Smoke Guest')
} catch (e) {
  fail(`creating or joining a game: ${e.message}`)
}
await until('the host to see the guest join', () => host.state?.players?.length === 2)
step(`game ${joined.code} created and joined; both players see each other`)

await Promise.all([host.conn.stop(), guest.conn.stop()])
console.log('Smoke test passed.')
process.exit(0)
