import { readFileSync, writeFileSync, renameSync, mkdirSync } from 'node:fs'
import { dirname } from 'node:path'

const fold = (s) => (s ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

const FILE_TYPES = ['documentMessage', 'imageMessage', 'videoMessage', 'audioMessage', 'documentWithCaptionMessage']

export function unwrap(message) {
  let m = message ?? {}
  for (let i = 0; i < 4; i++) {
    const inner = m.ephemeralMessage?.message ?? m.viewOnceMessage?.message ?? m.viewOnceMessageV2?.message ?? m.documentWithCaptionMessage?.message
    if (!inner) break
    m = inner
  }
  return m
}

export function textOf(message) {
  const m = unwrap(message)
  return m.conversation ?? m.extendedTextMessage?.text ?? m.imageMessage?.caption ?? m.videoMessage?.caption ?? m.documentMessage?.caption
    ?? (m.imageMessage ? '📷 Foto' : m.videoMessage ? '🎬 Video' : m.audioMessage ? '🎤 Audio' : m.documentMessage ? `📄 ${m.documentMessage.fileName ?? 'Documento'}` : m.stickerMessage ? 'Sticker' : '')
}

export function fileOf(message) {
  const m = unwrap(message)
  const key = FILE_TYPES.find((k) => m[k])
  if (!key) return null
  const f = m[key]
  const ext = { imageMessage: 'jpg', videoMessage: 'mp4', audioMessage: 'ogg' }[key]
  return {
    fileName: f.fileName ?? `${key.replace('Message', '')}_${Date.now()}.${ext ?? 'bin'}`,
    mime: f.mimetype ?? 'application/octet-stream',
    size: Number(f.fileLength ?? 0),
  }
}

const tsOf = (t) => Number(typeof t === 'object' && t !== null ? (t.low ?? t.toNumber?.() ?? 0) : (t ?? 0))

export function createStore(file, { maxChats = 60, maxMessagesPerChat = 40, debounceMs = 2000 } = {}) {
  let state = { chats: {}, messages: {}, contacts: {} }
  try {
    state = { ...state, ...JSON.parse(readFileSync(file, 'utf8')) }
  } catch {}
  let timer = null

  const schedule = () => {
    if (timer || !file) return
    timer = setTimeout(() => {
      timer = null
      flush()
    }, debounceMs)
    timer.unref?.()
  }

  function flush() {
    if (!file) return
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
    mkdirSync(dirname(file), { recursive: true })
    writeFileSync(file + '.tmp', JSON.stringify(state))
    renameSync(file + '.tmp', file)
  }

  function prune() {
    const jids = Object.keys(state.chats).sort((a, b) => state.chats[b].lastTs - state.chats[a].lastTs)
    for (const jid of jids.slice(maxChats)) {
      delete state.chats[jid]
      delete state.messages[jid]
    }
  }

  function nameOf(jid) {
    return state.contacts[jid]?.name ?? state.chats[jid]?.name ?? jid.split('@')[0]
  }

  function upsertChats(chats) {
    for (const c of chats ?? []) {
      if (!c?.id || c.id === 'status@broadcast') continue
      const prev = state.chats[c.id] ?? { jid: c.id, name: '', lastText: '', lastTs: 0, unread: 0, isGroup: c.id.endsWith('@g.us') }
      state.chats[c.id] = {
        ...prev,
        name: c.name ?? c.subject ?? prev.name,
        unread: c.unreadCount ?? prev.unread,
        lastTs: Math.max(prev.lastTs, tsOf(c.conversationTimestamp)),
      }
    }
    prune()
    schedule()
  }

  function upsertContacts(contacts) {
    for (const c of contacts ?? []) {
      if (!c?.id) continue
      const name = c.name ?? c.notify ?? c.verifiedName
      if (name) state.contacts[c.id] = { jid: c.id, name }
    }
    schedule()
  }

  function upsertMessages(msgs, { countUnread = false } = {}) {
    const changed = []
    for (const m of msgs ?? []) {
      const jid = m?.key?.remoteJid
      if (!jid || jid === 'status@broadcast' || !m.message) continue
      const ts = tsOf(m.messageTimestamp)
      const item = { id: m.key.id, fromMe: !!m.key.fromMe, text: textOf(m.message), ts, file: fileOf(m.message), sender: m.pushName ?? null }
      const list = (state.messages[jid] ??= [])
      const i = list.findIndex((x) => x.id === item.id)
      if (i >= 0) list[i] = item
      else list.push(item)
      list.sort((a, b) => a.ts - b.ts)
      if (list.length > maxMessagesPerChat) list.splice(0, list.length - maxMessagesPerChat)
      const chat = (state.chats[jid] ??= { jid, name: '', lastText: '', lastTs: 0, unread: 0, isGroup: jid.endsWith('@g.us') })
      if (!item.fromMe && m.pushName && !jid.endsWith('@g.us') && !state.contacts[jid]) state.contacts[jid] = { jid, name: m.pushName }
      if (ts >= chat.lastTs) {
        chat.lastTs = ts
        chat.lastText = item.text
      }
      if (countUnread && !item.fromMe && i < 0) chat.unread += 1
      changed.push({ chat: jid, message: item })
    }
    prune()
    schedule()
    return changed
  }

  function ingestHistory({ chats, contacts, messages } = {}) {
    upsertContacts(contacts)
    upsertChats(chats)
    upsertMessages(messages)
  }

  const chatView = (c) => ({ ...c, name: nameOf(c.jid) })

  return {
    ingestHistory,
    upsertChats,
    upsertContacts,
    upsertMessages,
    markRead(jid) {
      if (state.chats[jid]) state.chats[jid].unread = 0
      schedule()
    },
    recentChats: (n = 8) => Object.values(state.chats).filter((c) => c.lastTs > 0).sort((a, b) => b.lastTs - a.lastTs).slice(0, n).map(chatView),
    messages: (jid, n = 20) => (state.messages[jid] ?? []).slice(-n),
    findMessage: (jid, id) => (state.messages[jid] ?? []).find((m) => m.id === id) ?? null,
    recentFiles(n = 8) {
      const all = []
      for (const [jid, list] of Object.entries(state.messages))
        for (const m of list) if (m.file) all.push({ jid, chatName: nameOf(jid), messageId: m.id, ts: m.ts, ...m.file })
      return all.sort((a, b) => b.ts - a.ts).slice(0, n)
    },
    searchContacts(q, n = 12) {
      const f = fold(q)
      const seen = new Map()
      for (const c of Object.values(state.contacts)) seen.set(c.jid, c.name)
      for (const c of Object.values(state.chats)) if (!seen.has(c.jid)) seen.set(c.jid, nameOf(c.jid))
      return [...seen].filter(([jid, name]) => fold(name).includes(f) || jid.includes(f)).slice(0, n).map(([jid, name]) => ({ jid, name }))
    },
    unreadTotal: () => Object.values(state.chats).reduce((a, c) => a + (c.unread ?? 0), 0),
    flush,
  }
}
