import { rmSync, mkdirSync, writeFileSync, statSync, readFileSync } from 'node:fs'
import { join, basename, extname } from 'node:path'
import makeWASocket, { useMultiFileAuthState, DisconnectReason, Browsers, downloadMediaMessage, fetchLatestBaileysVersion } from 'baileys'
import QRCode from 'qrcode'
import pino from 'pino'
import { RpcError } from './protocol.mjs'

const MIME = { '.pdf': 'application/pdf', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif', '.mp4': 'video/mp4', '.mov': 'video/quicktime', '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.m4a': 'audio/mp4', '.wav': 'audio/wav', '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation', '.zip': 'application/zip', '.txt': 'text/plain' }
const MAX_FILE = 100 * 1024 * 1024

export function createConnection({ authDir, store, emit, downloads, logger = pino({ level: 'silent' }) }) {
  let sock = null
  let me = null
  let state = 'idle'
  let stopping = false
  let retry = 1000
  const raw = new Map()
  const queue = []
  let sending = false

  const remember = (msgs) => {
    for (const m of msgs ?? []) if (m?.key?.id && m.message) raw.set(m.key.remoteJid + '|' + m.key.id, m)
    if (raw.size > 2000) for (const k of [...raw.keys()].slice(0, raw.size - 2000)) raw.delete(k)
  }

  async function start() {
    if (sock) return
    stopping = false
    state = 'connecting'
    mkdirSync(authDir, { recursive: true })
    const { state: auth, saveCreds } = await useMultiFileAuthState(authDir)
    let version
    try {
      version = (await fetchLatestBaileysVersion()).version
    } catch {}
    sock = makeWASocket({ auth, logger, version, browser: Browsers.windows('kyzWheel'), syncFullHistory: false, markOnlineOnConnect: false, generateHighQualityLinkPreview: false })
    sock.ev.on('creds.update', saveCreds)
    sock.ev.on('connection.update', async (u) => {
      if (u.qr) {
        state = 'qr'
        emit('qr', { png: await QRCode.toDataURL(u.qr, { margin: 1, width: 280, color: { dark: '#0b0b10', light: '#ffffff' } }) })
      }
      if (u.connection === 'open') {
        state = 'open'
        retry = 1000
        me = { jid: sock.user?.id, name: sock.user?.name ?? '' }
        emit('connected', { me })
      }
      if (u.connection === 'close') {
        const code = u.lastDisconnect?.error?.output?.statusCode
        sock = null
        if (code === DisconnectReason.loggedOut) {
          state = 'logged_out'
          rmSync(authDir, { recursive: true, force: true })
          emit('disconnected', { reason: 'logged_out' })
          return
        }
        state = 'reconnecting'
        emit('disconnected', { reason: 'reconnecting', code })
        if (!stopping) setTimeout(() => start().catch(() => {}), retry).unref?.()
        retry = Math.min(30000, retry * 2)
      }
    })
    sock.ev.on('messaging-history.set', (h) => {
      remember(h.messages)
      store.ingestHistory(h)
      emit('chats.changed', {})
    })
    sock.ev.on('chats.upsert', (c) => {
      store.upsertChats(c)
      emit('chats.changed', {})
    })
    sock.ev.on('chats.update', (c) => {
      store.upsertChats(c)
      emit('chats.changed', {})
    })
    sock.ev.on('contacts.upsert', (c) => store.upsertContacts(c))
    sock.ev.on('contacts.update', (c) => store.upsertContacts(c))
    sock.ev.on('messages.upsert', ({ messages, type }) => {
      remember(messages)
      const changed = store.upsertMessages(messages, { countUnread: type === 'notify' })
      for (const c of changed) if (type === 'notify') emit('message.new', c)
      if (changed.length) emit('chats.changed', {})
    })
  }

  const requireOpen = () => {
    if (state !== 'open' || !sock) throw new RpcError('not_connected', 'WhatsApp no está conectado')
  }

  async function pump() {
    if (sending) return
    sending = true
    while (queue.length) {
      const job = queue.shift()
      try {
        job.resolve(await job.run())
      } catch (e) {
        job.reject(e)
      }
      await new Promise((r) => setTimeout(r, 1000))
    }
    sending = false
  }

  const enqueue = (run) => new Promise((resolve, reject) => {
    queue.push({ run, resolve, reject })
    pump()
  })

  return {
    status: () => ({ state, me }),
    async login() {
      await start()
      return { state }
    },
    async logout() {
      stopping = true
      try {
        await sock?.logout()
      } catch {}
      sock = null
      state = 'logged_out'
      rmSync(authDir, { recursive: true, force: true })
      return { state }
    },
    async stop() {
      stopping = true
      store.flush()
      sock?.end?.(undefined)
      sock = null
    },
    async sendText({ jid, text }) {
      requireOpen()
      if (!jid || !text?.trim()) throw new RpcError('bad_request', 'Falta chat o texto')
      const sent = await enqueue(() => sock.sendMessage(jid, { text }))
      remember([sent])
      store.upsertMessages([sent])
      store.markRead(jid)
      return { id: sent?.key?.id }
    },
    async sendFile({ jid, path, caption }) {
      requireOpen()
      const size = statSync(path).size
      if (size > MAX_FILE) throw new RpcError('too_large', 'El archivo pasa de 100 MB')
      const ext = extname(path).toLowerCase()
      const mimetype = MIME[ext] ?? 'application/octet-stream'
      const buffer = readFileSync(path)
      const content = mimetype.startsWith('image/') && ext !== '.gif'
        ? { image: buffer, caption, mimetype }
        : mimetype.startsWith('video/')
          ? { video: buffer, caption, mimetype }
          : { document: buffer, fileName: basename(path), mimetype, caption }
      const sent = await enqueue(() => sock.sendMessage(jid, content))
      remember([sent])
      store.upsertMessages([sent])
      return { id: sent?.key?.id }
    },
    async download({ jid, messageId }) {
      requireOpen()
      const m = raw.get(jid + '|' + messageId)
      const info = store.findMessage(jid, messageId)
      if (!m || !info?.file) throw new RpcError('not_found', 'Ese archivo ya no está disponible, ábrelo en WhatsApp')
      const buffer = await downloadMediaMessage(m, 'buffer', {}, { logger, reuploadRequest: sock.updateMediaMessage })
      mkdirSync(downloads, { recursive: true })
      const safe = info.file.fileName.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_')
      let target = join(downloads, safe)
      for (let i = 1; ; i++) {
        try {
          statSync(target)
          target = join(downloads, safe.replace(/(\.[^.]*)?$/, `_${i}$1`))
        } catch {
          break
        }
      }
      writeFileSync(target, buffer)
      return { path: target }
    },
    async markRead({ jid }) {
      store.markRead(jid)
      const last = store.messages(jid, 1)[0]
      if (state === 'open' && last && !last.fromMe) await sock.readMessages([{ remoteJid: jid, id: last.id, fromMe: false }]).catch(() => {})
      return { ok: true }
    },
  }
}
