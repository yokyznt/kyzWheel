import { readFileSync, writeFileSync, existsSync, mkdirSync, renameSync } from 'node:fs'
import { dirname } from 'node:path'

export const SERVICES = ['telegram', 'discord', 'instagram']

export function createImStore(file, { maxChats = 60, maxMessagesPerChat = 40, debounceMs = 1500 } = {}) {
  let state = { chats: {}, messages: {}, stickers: {} }
  try {
    if (existsSync(file)) state = { ...state, ...JSON.parse(readFileSync(file, 'utf8')) }
  } catch {}
  let timer = null
  const key = (svc, id) => `${svc}:${id}`

  function flush() {
    timer = null
    try {
      mkdirSync(dirname(file), { recursive: true })
      writeFileSync(file + '.tmp', JSON.stringify(state))
      renameSync(file + '.tmp', file)
    } catch {}
  }
  const schedule = () => { if (!timer) timer = setTimeout(flush, debounceMs) }

  function prune() {
    const chats = Object.values(state.chats).sort((a, b) => b.lastTs - a.lastTs)
    for (const c of chats.slice(maxChats)) {
      delete state.chats[c.key]
      delete state.messages[c.key]
    }
  }

  return {
    upsert(svc, chat, msg) {
      const k = key(svc, chat.id)
      const c = (state.chats[k] ??= { key: k, svc, id: String(chat.id), name: chat.name ?? '', isGroup: !!chat.isGroup, lastTs: 0, extra: {} })
      if (chat.name) c.name = chat.name
      if (chat.extra) c.extra = { ...c.extra, ...chat.extra }
      const list = (state.messages[k] ??= [])
      const item = { id: String(msg.id), fromMe: !!msg.fromMe, sender: msg.sender ?? null, text: msg.text ?? '', ts: Number(msg.ts) || 0 }
      if (msg.sticker) item.sticker = msg.sticker
      const i = list.findIndex((m) => m.id === item.id)
      const isNew = i < 0
      if (isNew) list.push(item)
      else list[i] = item
      list.sort((a, b) => a.ts - b.ts)
      if (list.length > maxMessagesPerChat) list.splice(0, list.length - maxMessagesPerChat)
      if (item.ts >= c.lastTs) c.lastTs = item.ts
      prune()
      schedule()
      return isNew
    },
    chat: (svc, id) => state.chats[key(svc, id)] ?? null,
    recent(n = 60, services = SERVICES) {
      const all = []
      for (const [k, list] of Object.entries(state.messages)) {
        const c = state.chats[k]
        if (!c || !services.includes(c.svc)) continue
        for (const m of list) all.push({ svc: c.svc, chatId: c.id, chatName: c.name, isGroup: c.isGroup, ...m })
      }
      return all.sort((a, b) => b.ts - a.ts).slice(0, n)
    },
    addSticker(svc, sticker) {
      const list = (state.stickers[svc] ??= [])
      const i = list.findIndex((s) => s.id === sticker.id)
      if (i >= 0) list.splice(i, 1)
      list.unshift(sticker)
      if (list.length > 30) list.length = 30
      schedule()
    },
    stickers: (svc) => state.stickers[svc] ?? [],
    clear(svc) {
      for (const [k, c] of Object.entries(state.chats)) if (c.svc === svc) { delete state.chats[k]; delete state.messages[k] }
      delete state.stickers[svc]
      schedule()
    },
    flush,
  }
}
