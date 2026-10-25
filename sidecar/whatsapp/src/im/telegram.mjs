import { TelegramClient, Api } from 'telegram'
import { StringSession } from 'telegram/sessions/index.js'
import { NewMessage } from 'telegram/events/index.js'
import { existsSync, writeFileSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { createAdapterBase, placeholder, qrPng } from './common.mjs'
import { RpcError } from '../protocol.mjs'

export function createTelegram({ authDir, cacheDir, store, emit }) {
  const base = createAdapterBase('telegram', { authDir, emit })
  let client = null
  let me = null
  const peers = new Map()
  const docs = new Map()

  const creds = () => {
    const saved = base.load() ?? {}
    const id = Number(process.env.KYZ_TG_API_ID || saved.apiId || 0)
    const hash = process.env.KYZ_TG_API_HASH || saved.apiHash || ''
    return { id, hash, saved }
  }

  const nameOf = (e) => e?.title || [e?.firstName, e?.lastName].filter(Boolean).join(' ') || (e?.username ? '@' + e.username : '') || 'Telegram'

  function kindOf(m) {
    if (m.sticker) return 'sticker'
    if (m.voice) return 'voice'
    if (m.audio) return 'audio'
    if (m.gif) return 'gif'
    if (m.video) return 'video'
    if (m.photo) return 'photo'
    if (m.document) return 'document'
    if (m.geo) return 'location'
    if (m.contact) return 'contact'
    if (m.poll) return 'poll'
    return null
  }

  async function ingest(m, chat, notify) {
    if (!m || m.className === 'MessageService') return
    const chatId = String(m.chatId ?? chat?.id)
    if (chat) peers.set(chatId, chat)
    const kind = kindOf(m)
    let sender = null
    if (!m.out && (m.isGroup || m.isChannel)) {
      try { sender = nameOf(await m.getSender()) } catch {}
    }
    const text = m.message || (kind ? placeholder(kind) : '')
    if (!text) return
    const extra = chat?.username ? { username: chat.username } : chat?.className === 'User' ? { userId: String(chat.id) } : {}
    const isNew = store.upsert('telegram', { id: chatId, name: nameOf(chat), isGroup: !!(m.isGroup || m.isChannel), extra }, { id: m.id, fromMe: !!m.out, sender, text, ts: m.date })
    if (isNew && notify && !m.out) emit('im.message', { svc: 'telegram', chatId, chatName: nameOf(chat), isGroup: !!(m.isGroup || m.isChannel), id: String(m.id), sender, text, ts: m.date })
  }

  async function history() {
    const dialogs = await client.getDialogs({ limit: 30 })
    for (const dlg of dialogs) peers.set(String(dlg.id), dlg.entity)
    for (const dlg of dialogs.filter((x) => !(x.isChannel && !x.isGroup)).slice(0, 14)) {
      try {
        const msgs = await client.getMessages(dlg.entity, { limit: 10 })
        for (const m of msgs) await ingest(m, dlg.entity, false)
      } catch {}
    }
    emit('im.changed', { svc: 'telegram' })
  }

  async function ready() {
    me = await client.getMe()
    base.save({ ...creds().saved, session: client.session.save() })
    base.set({ state: 'open', me: nameOf(me) })
    client.addEventHandler(async (ev) => {
      try {
        const chat = await ev.message.getChat()
        await ingest(ev.message, chat, true)
        emit('im.changed', { svc: 'telegram' })
      } catch {}
    }, new NewMessage({}))
    history().catch(() => {})
  }

  async function start(params = {}) {
    if (params.apiId && params.apiHash) base.save({ ...creds().saved, apiId: Number(params.apiId), apiHash: String(params.apiHash).trim() })
    const { id, hash, saved } = creds()
    if (!id || !hash) {
      base.set({ state: 'need', need: 'apiid' })
      return base.status
    }
    if (client) try { await client.disconnect() } catch {}
    base.set({ state: 'connecting' })
    client = new TelegramClient(new StringSession(saved.session ?? ''), id, hash, { connectionRetries: 5, deviceModel: 'kyzWheel', appVersion: '1.0', systemVersion: 'Windows' })
    client.setLogLevel('none')
    await client.connect()
    if (await client.checkAuthorization()) {
      await ready()
      return base.status
    }
    client.signInUserWithQrCode({ apiId: id, apiHash: hash }, {
      qrCode: async ({ token }) => base.set({ state: 'qr', qr: await qrPng('tg://login?token=' + Buffer.from(token).toString('base64url')) }),
      password: async (hint) => base.ask('password', hint),
      onError: async (e) => {
        base.set({ state: 'error', error: e?.message ?? String(e) })
        return true
      },
    }).then(ready).catch((e) => base.set({ state: 'error', error: e?.message ?? String(e) }))
    return base.status
  }

  async function peer(chatId) {
    if (peers.has(chatId)) return peers.get(chatId)
    const e = await client.getEntity(chatId)
    peers.set(chatId, e)
    return e
  }

  return {
    svc: 'telegram',
    get status() { return base.status },
    autostart: () => (creds().saved.session ? start().catch((e) => base.set({ state: 'error', error: e.message })) : null),
    login: (p) => start(p),
    submit: (v) => base.submit(v),
    async logout() {
      base.cancel()
      try { await client?.invoke(new Api.auth.LogOut()) } catch {}
      try { await client?.disconnect() } catch {}
      client = null
      const { saved } = creds()
      base.save({ apiId: saved.apiId, apiHash: saved.apiHash })
      store.clear('telegram')
      base.set({ state: 'off' })
    },
    async send(chatId, text) {
      base.requireOpen()
      const m = await client.sendMessage(await peer(chatId), { message: text })
      await ingest(m, await peer(chatId), false)
      return { id: String(m.id) }
    },
    async stickers() {
      base.requireOpen()
      const dir = join(cacheDir, 'stickers')
      mkdirSync(dir, { recursive: true })
      const res = await client.invoke(new Api.messages.GetRecentStickers({ hash: 0 }))
      const out = []
      for (const doc of (res.stickers ?? []).filter((x) => x.mimeType === 'image/webp').slice(0, 24)) {
        const id = String(doc.id)
        docs.set(id, doc)
        const path = join(dir, `tg-${id}.webp`)
        if (!existsSync(path)) {
          try { writeFileSync(path, await client.downloadMedia(doc)) } catch { continue }
        }
        out.push({ id, path })
      }
      return out
    },
    async sendSticker(chatId, id) {
      base.requireOpen()
      const doc = docs.get(id)
      if (!doc) throw new RpcError('bad_request', 'Sticker no encontrado')
      const p = await peer(chatId)
      const m = await client.sendFile(p, { file: doc })
      await ingest(m, p, false)
      return { id: String(m.id) }
    },
    callUrl(chatId) {
      const c = store.chat('telegram', chatId)
      if (c?.extra?.username) return `tg://resolve?domain=${c.extra.username}`
      return `tg://user?id=${c?.extra?.userId ?? chatId}`
    },
  }
}
