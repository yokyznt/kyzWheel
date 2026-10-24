import { readFileSync, writeFileSync, renameSync, mkdirSync } from 'node:fs'
import { dirname } from 'node:path'

const fold = (s) => (s ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()

const MEDIA = { imageMessage: 'image', videoMessage: 'video', ptvMessage: 'video', audioMessage: 'audio', stickerMessage: 'sticker', documentMessage: 'document' }
const EXT = { image: 'jpg', video: 'mp4', audio: 'ogg', sticker: 'webp', document: 'bin' }
const SKIP = new Set(['protocolMessage', 'reactionMessage', 'pollUpdateMessage', 'senderKeyDistributionMessage', 'messageContextInfo', 'keepInChatMessage', 'encReactionMessage'])
const MAX_THUMB = 16 * 1024

const b64 = (v) => (v == null ? null : typeof v === 'string' ? v : Buffer.from(v).toString('base64'))

export function unwrap(message) {
  let m = message ?? {}
  for (let i = 0; i < 5; i++) {
    const inner = m.ephemeralMessage?.message ?? m.viewOnceMessage?.message ?? m.viewOnceMessageV2?.message ?? m.viewOnceMessageV2Extension?.message ?? m.documentWithCaptionMessage?.message ?? m.editedMessage?.message
    if (!inner) break
    m = inner
  }
  return m
}

export function isContent(message) {
  return Object.keys(unwrap(message)).some((k) => !SKIP.has(k))
}

export function captionOf(message) {
  const m = unwrap(message)
  return m.conversation ?? m.extendedTextMessage?.text ?? m.imageMessage?.caption ?? m.videoMessage?.caption ?? m.documentMessage?.caption ?? ''
}

export function textOf(message) {
  const m = unwrap(message)
  const c = captionOf(message)
  if (c) return c
  if (m.imageMessage) return '📷 Foto'
  if (m.videoMessage || m.ptvMessage) return '🎬 Video'
  if (m.audioMessage) return m.audioMessage.ptt ? '🎤 Nota de voz' : '🎵 Audio'
  if (m.stickerMessage) return '🩷 Sticker'
  if (m.documentMessage) return `📄 ${m.documentMessage.fileName ?? 'Documento'}`
  if (m.locationMessage || m.liveLocationMessage) return '📍 Ubicación'
  if (m.contactMessage) return `👤 ${m.contactMessage.displayName ?? 'Contacto'}`
  if (m.contactsArrayMessage) return '👥 Contactos'
  if (m.pollCreationMessage || m.pollCreationMessageV3) return `📊 ${(m.pollCreationMessage ?? m.pollCreationMessageV3).name ?? 'Encuesta'}`
  if (m.eventMessage) return `📅 ${m.eventMessage.name ?? 'Evento'}`
  return ''
}

export function fileOf(message) {
  const m = unwrap(message)
  const key = Object.keys(MEDIA).find((k) => m[k])
  if (!key) return null
  const f = m[key]
  const kind = MEDIA[key]
  const thumb = b64(f.jpegThumbnail)
  const ext = kind === 'audio' && f.mimetype?.includes('mpeg') ? 'mp3' : EXT[kind]
  return {
    kind,
    fileName: f.fileName ?? `${kind}_${Number(f.mediaKeyTimestamp?.low ?? f.mediaKeyTimestamp ?? Date.now())}.${ext}`,
    mime: f.mimetype ?? 'application/octet-stream',
    size: Number(f.fileLength?.low ?? f.fileLength ?? 0),
    seconds: Number(f.seconds ?? 0),
    ptt: !!f.ptt,
    width: Number(f.width ?? 0),
    height: Number(f.height ?? 0),
    thumb: thumb && thumb.length <= MAX_THUMB * 1.4 ? thumb : null,
    media: f.mediaKey && (f.directPath || f.url) ? { mediaKey: b64(f.mediaKey), directPath: f.directPath ?? null, url: f.url ?? null } : null,
  }
}

const tsOf = (t) => Number(typeof t === 'object' && t !== null ? (t.low ?? t.toNumber?.() ?? 0) : (t ?? 0))
const isPn = (j) => j?.endsWith('@s.whatsapp.net')
const isLid = (j) => j?.endsWith('@lid')
const bare = (j) => (j ?? '').replace(/:\d+@/, '@')

export function createStore(file, { maxChats = 80, maxMessagesPerChat = 60, debounceMs = 2000 } = {}) {
  let state = { chats: {}, messages: {}, contacts: {}, alias: {}, push: {} }
  try {
    state = { ...state, ...JSON.parse(readFileSync(file, 'utf8')) }
  } catch {}
  state.alias ??= {}
  state.push ??= {}
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

  function addAlias(a, b) {
    a = bare(a)
    b = bare(b)
    if (!a || !b || a === b) return
    state.alias[a] = b
    state.alias[b] = a
    schedule()
  }

  const ids = (jid) => {
    const j = bare(jid)
    return state.alias[j] ? [j, state.alias[j]] : [j]
  }

  function contactName(jid) {
    let fallback = null
    for (const j of ids(jid)) {
      const c = state.contacts[j]
      if (c?.saved && c.name) return c.name
      fallback ??= c?.name ?? null
    }
    return fallback
  }

  function phoneOf(jid) {
    const pn = ids(jid).find(isPn)
    return pn ? '+' + pn.split('@')[0] : null
  }

  function nameOf(jid) {
    const j = bare(jid)
    if (j.endsWith('@g.us')) return state.chats[j]?.name || 'Grupo'
    const saved = ids(j).map((x) => state.contacts[x]).find((c) => c?.saved && c.name)
    if (saved) return saved.name
    return state.chats[j]?.name || contactName(j) || ids(j).map((x) => state.push[x]).find(Boolean) || phoneOf(j) || 'Contacto'
  }

  function upsertChats(chats) {
    for (const c of chats ?? []) {
      if (!c?.id || c.id === 'status@broadcast' || c.id.endsWith('@newsletter')) continue
      const prev = state.chats[c.id] ?? { jid: c.id, name: '', lastText: '', lastTs: 0, unread: 0, isGroup: c.id.endsWith('@g.us') }
      if (c.pnJid) addAlias(c.id, c.pnJid)
      if (c.lidJid) addAlias(c.id, c.lidJid)
      state.chats[c.id] = {
        ...prev,
        name: c.subject ?? c.name ?? prev.name,
        unread: c.unreadCount ?? prev.unread,
        lastTs: Math.max(prev.lastTs, tsOf(c.conversationTimestamp)),
      }
    }
    prune()
    schedule()
  }

  function setGroupNames(groups) {
    for (const [jid, g] of Object.entries(groups ?? {})) {
      if (!g?.subject) continue
      if (state.chats[jid]) state.chats[jid].name = g.subject
      else state.chats[jid] = { jid, name: g.subject, lastText: '', lastTs: 0, unread: 0, isGroup: true }
    }
    schedule()
  }

  function upsertContacts(contacts) {
    for (const c of contacts ?? []) {
      if (!c?.id) continue
      const all = [c.id, c.lid, c.phoneNumber, c.jid].filter(Boolean).map(bare)
      for (let i = 1; i < all.length; i++) addAlias(all[0], all[i])
      const saved = !!c.name
      const name = c.name ?? c.notify ?? c.verifiedName
      if (!name) continue
      for (const j of all) {
        const prev = state.contacts[j]
        if (prev?.saved && !saved) continue
        state.contacts[j] = { jid: j, name, saved }
      }
      if (!saved && c.notify) for (const j of all) state.push[j] = c.notify
    }
    schedule()
  }

  function upsertMessages(msgs, { countUnread = false } = {}) {
    const changed = []
    for (const m of msgs ?? []) {
      const jid = m?.key?.remoteJid
      if (!jid || jid === 'status@broadcast' || jid.endsWith('@newsletter') || !m.message || !isContent(m.message)) continue
      if (m.key.remoteJidAlt) addAlias(jid, m.key.remoteJidAlt)
      if (m.key.participant && m.key.participantAlt) addAlias(m.key.participant, m.key.participantAlt)
      const group = jid.endsWith('@g.us')
      const from = group ? m.key.participant : jid
      if (!m.key.fromMe && m.pushName && from) state.push[bare(from)] = m.pushName
      const ts = tsOf(m.messageTimestamp)
      const sender = group && !m.key.fromMe && from ? (contactName(from) ?? m.pushName ?? phoneOf(from)) : (m.pushName ?? null)
      const item = { id: m.key.id, fromMe: !!m.key.fromMe, text: textOf(m.message), caption: captionOf(m.message), ts, file: fileOf(m.message), sender }
      const list = (state.messages[jid] ??= [])
      const i = list.findIndex((x) => x.id === item.id)
      if (i >= 0) list[i] = item
      else list.push(item)
      list.sort((a, b) => a.ts - b.ts)
      if (list.length > maxMessagesPerChat) list.splice(0, list.length - maxMessagesPerChat)
      const chat = (state.chats[jid] ??= { jid, name: '', lastText: '', lastTs: 0, unread: 0, isGroup: group })
      if (ts >= chat.lastTs) {
        chat.lastTs = ts
        chat.lastText = (group && sender && !item.fromMe ? sender.split(' ')[0] + ': ' : item.fromMe ? 'Tú: ' : '') + item.text
      }
      if (countUnread && !item.fromMe && i < 0) chat.unread += 1
      changed.push({ chat: jid, message: item })
    }
    prune()
    schedule()
    return changed
  }

  function ingestHistory({ chats, contacts, messages, lidPnMappings } = {}) {
    for (const x of lidPnMappings ?? []) addAlias(x.lid, x.pn)
    upsertContacts(contacts)
    upsertChats(chats)
    upsertMessages(messages)
  }

  const chatView = (c) => ({ ...c, name: nameOf(c.jid), phone: phoneOf(c.jid) })
  const lean = (m) => (m.file ? { ...m, file: { ...m.file, media: undefined } } : m)

  return {
    ingestHistory,
    upsertChats,
    upsertContacts,
    upsertMessages,
    setGroupNames,
    addAlias,
    nameOf,
    markRead(jid) {
      if (state.chats[jid]) state.chats[jid].unread = 0
      schedule()
    },
    recentChats: (n = 8) => Object.values(state.chats).filter((c) => c.lastTs > 0).sort((a, b) => b.lastTs - a.lastTs).slice(0, n).map(chatView),
    messages: (jid, n = 20) => (state.messages[jid] ?? []).slice(-n).map(lean),
    recentMessages(n = 30) {
      const all = []
      for (const [jid, list] of Object.entries(state.messages))
        for (const m of list) all.push({ jid, chatName: nameOf(jid), isGroup: jid.endsWith('@g.us'), ...lean(m) })
      return all.sort((a, b) => b.ts - a.ts).slice(0, n)
    },
    findMessage: (jid, id) => (state.messages[jid] ?? []).find((m) => m.id === id) ?? null,
    recentFiles(n = 8) {
      const all = []
      for (const [jid, list] of Object.entries(state.messages))
        for (const m of list) if (m.file && m.file.kind !== 'sticker') all.push({ jid, chatName: nameOf(jid), messageId: m.id, ts: m.ts, ...m.file, media: undefined })
      return all.sort((a, b) => b.ts - a.ts).slice(0, n)
    },
    searchContacts(q, n = 12) {
      const f = fold(q)
      const seen = new Map()
      for (const c of Object.values(state.chats)) seen.set(c.jid, nameOf(c.jid))
      for (const c of Object.values(state.contacts)) {
        if (!c.saved) continue
        const key = isLid(c.jid) && state.alias[c.jid] && seen.has(state.alias[c.jid]) ? state.alias[c.jid] : c.jid
        if (!seen.has(key)) seen.set(key, c.name)
      }
      return [...seen].filter(([jid, name]) => fold(name).includes(f) || jid.includes(f)).slice(0, n).map(([jid, name]) => ({ jid, name }))
    },
    unreadTotal: () => Object.values(state.chats).reduce((a, c) => a + (c.unread ?? 0), 0),
    flush,
  }
}
