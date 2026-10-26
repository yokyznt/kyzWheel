import { createInterface } from 'node:readline'
import { join } from 'node:path'
import { homedir } from 'node:os'
import { createDispatcher, encodeEvent } from './protocol.mjs'
import { createStore } from './store.mjs'
import { createConnection } from './connection.mjs'
import { createMessengers } from './im/index.mjs'

console.log = console.info = console.debug = console.warn = (...a) => process.stderr.write(a.map(String).join(' ') + '\n')

const appData = process.env.APPDATA ?? join(homedir(), 'AppData', 'Roaming')
const localData = process.env.LOCALAPPDATA ?? join(homedir(), 'AppData', 'Local')
const authDir = process.env.KYZ_WA_AUTH ?? join(appData, 'kyzWheel', 'wa-auth')
const storeFile = process.env.KYZ_WA_STORE ?? join(localData, 'kyzWheel.Data', 'wa-store.json')
const downloads = process.env.KYZ_WA_DOWNLOADS ?? join(homedir(), 'Downloads', 'kyzWheel')
const cacheDir = process.env.KYZ_WA_CACHE ?? join(localData, 'kyzWheel.Data', 'wa-cache')

const out = (line) => process.stdout.write(line + '\n')
const emit = (event, data) => out(encodeEvent(event, data))
const store = createStore(storeFile)
const conn = createConnection({ authDir, store, emit, downloads, cacheDir })
const im = createMessengers({ authDir: process.env.KYZ_IM_AUTH ?? join(appData, 'kyzWheel', 'im-auth'), dataDir: join(localData, 'kyzWheel.Data'), cacheDir, emit })

const dispatch = createDispatcher({
  status: async () => ({ ...conn.status(), unread: store.unreadTotal() }),
  login: () => conn.login(),
  logout: () => conn.logout(),
  'chats.recent': async ({ limit = 8 }) => store.recentChats(limit),
  'messages.list': async ({ jid, limit = 20 }) => store.messages(jid, limit),
  'messages.recent': async ({ limit = 30 }) => store.recentMessages(limit),
  'messages.send': (p) => conn.sendText(p),
  'files.send': (p) => conn.sendFile(p),
  'files.recent': async ({ limit = 8 }) => store.recentFiles(limit),
  'media.download': (p) => conn.download(p),
  'media.cache': (p) => conn.media(p),
  'avatar.get': (p) => conn.avatar(p),
  'chats.read': (p) => conn.markRead(p),
  ...im.handlers,
  'stickers.recent': (p) => conn.recentStickers(p),
  'stickers.send': (p) => conn.sendSticker(p),
  'contacts.search': async ({ q = '', limit = 12 }) => store.searchContacts(q, limit),
})

const rl = createInterface({ input: process.stdin })
rl.on('line', async (line) => {
  if (line.trim()) out(await dispatch(line))
})
rl.on('close', async () => {
  im.flush()
  await conn.stop()
  process.exit(0)
})
process.on('uncaughtException', (e) => process.stderr.write(String(e?.stack ?? e) + '\n'))
process.on('unhandledRejection', (e) => process.stderr.write(String(e?.stack ?? e) + '\n'))
im.start()
if (process.argv.includes('--autologin')) conn.login().catch((e) => process.stderr.write(String(e) + '\n'))
