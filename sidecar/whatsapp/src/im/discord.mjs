import { Client } from 'discord.js-selfbot-v13'
import RemoteAuth from 'discord.js-selfbot-v13/src/util/RemoteAuth.js'
import { existsSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { createAdapterBase, placeholder, qrPng, download } from './common.mjs'

const isDm = (c) => c?.type === 'DM' || c?.type === 'GROUP_DM'

export function createDiscord({ authDir, cacheDir, store, emit }) {
  const base = createAdapterBase('discord', { authDir, emit })
  let client = null

  const chatName = (c) => c.type === 'DM' ? (c.recipient?.globalName || c.recipient?.username || 'Discord') : c.type === 'GROUP_DM' ? (c.name || [...(c.recipients?.values?.() ?? [])].map((u) => u.globalName || u.username).join(', ') || 'Grupo') : `#${c.name} · ${c.guild?.name ?? ''}`

  async function rememberStickers(m) {
    const dir = join(cacheDir, 'stickers')
    for (const s of m.stickers?.values?.() ?? []) {
      if (s.format !== 'PNG' && s.format !== 'APNG') continue
      mkdirSync(dir, { recursive: true })
      const path = join(dir, `dc-${s.id}.png`)
      try {
        if (!existsSync(path)) await download(s.url, path)
        store.addSticker('discord', { id: s.id, name: s.name, path })
      } catch {}
    }
  }

  function ingest(m, notify) {
    const c = m.channel
    if (!isDm(c) && !(client.user && m.mentions?.has?.(client.user))) return
    const fromMe = m.author?.id === client.user?.id
    const kind = m.stickers?.size ? 'sticker' : m.attachments?.size ? (m.attachments.first().contentType?.startsWith('image') ? 'photo' : m.attachments.first().contentType?.startsWith('video') ? 'video' : 'document') : null
    const text = m.content || (kind ? placeholder(kind) : '')
    if (!text) return
    const group = c.type !== 'DM'
    const sender = group && !fromMe ? (m.member?.displayName || m.author?.globalName || m.author?.username) : null
    const extra = c.guildId ? { guildId: c.guildId } : {}
    const isNew = store.upsert('discord', { id: c.id, name: chatName(c), isGroup: group, extra }, { id: m.id, fromMe, sender, text, ts: Math.floor(m.createdTimestamp / 1000) })
    if (m.stickers?.size) rememberStickers(m).catch(() => {})
    if (isNew && notify && !fromMe) emit('im.message', { svc: 'discord', chatId: c.id, chatName: chatName(c), isGroup: group, id: m.id, sender, text, ts: Math.floor(m.createdTimestamp / 1000) })
  }

  async function history() {
    const dms = [...client.channels.cache.values()].filter((c) => isDm(c) && c.lastMessageId).sort((a, b) => (BigInt(b.lastMessageId) > BigInt(a.lastMessageId) ? 1 : -1)).slice(0, 14)
    for (const c of dms) {
      try {
        const msgs = await c.messages.fetch({ limit: 10 })
        for (const m of msgs.values()) ingest(m, false)
      } catch {}
    }
    emit('im.changed', { svc: 'discord' })
  }

  function makeClient() {
    const c = new Client({ checkUpdate: false })
    c.on('ready', () => {
      base.save({ token: c.token })
      base.set({ state: 'open', me: c.user?.globalName || c.user?.username })
      history().catch(() => {})
    })
    c.on('messageCreate', (m) => {
      try {
        ingest(m, true)
        emit('im.changed', { svc: 'discord' })
      } catch {}
    })
    c.on('error', () => {})
    return c
  }

  async function start() {
    if (client) try { client.destroy() } catch {}
    client = makeClient()
    const saved = base.load()
    base.set({ state: 'connecting' })
    if (saved?.token) {
      client.login(saved.token).catch((e) => {
        base.forget()
        base.set({ state: 'error', error: 'La sesión de Discord expiró: vuelve a escanear el QR. ' + (e?.message ?? '') })
      })
      return base.status
    }
    const ra = new RemoteAuth()
    ra.on('ready', async () => base.set({ state: 'qr', qr: await qrPng(ra.AuthURL) }))
    ra.on('pending', (u) => base.set({ state: 'connecting', hint: `Confirma en el celular (${u?.username ?? ''})` }))
    ra.on('cancel', () => base.set({ state: 'error', error: 'Cancelado en el celular' }))
    ra.on('error', (e) => base.set({ state: 'error', error: e?.message ?? String(e) }))
    ra.connect(client).catch((e) => base.set({ state: 'error', error: e?.message ?? String(e) }))
    return base.status
  }

  const channel = (id) => client.channels.fetch(id)

  return {
    svc: 'discord',
    get status() { return base.status },
    autostart: () => (base.load()?.token ? start() : null),
    login: () => start(),
    submit: (v) => base.submit(v),
    async logout() {
      try { client?.destroy() } catch {}
      client = null
      base.forget()
      store.clear('discord')
      base.set({ state: 'off' })
    },
    async send(chatId, text) {
      base.requireOpen()
      const m = await (await channel(chatId)).send(text)
      ingest(m, false)
      return { id: m.id }
    },
    async stickers() {
      base.requireOpen()
      return store.stickers('discord')
    },
    async sendSticker(chatId, id) {
      base.requireOpen()
      const m = await (await channel(chatId)).send({ stickers: [id] })
      ingest(m, false)
      return { id: m.id }
    },
    callUrl(chatId) {
      const c = store.chat('discord', chatId)
      return `discord://-/channels/${c?.extra?.guildId ?? '@me'}/${chatId}`
    },
  }
}
