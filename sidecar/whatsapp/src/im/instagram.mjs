import { randomUUID } from 'node:crypto'
import { createAdapterBase, placeholder } from './common.mjs'
import { RpcError } from '../protocol.mjs'

const KIND = { media: 'photo', media_share: 'post', clip: 'reel', reel_share: 'reel', story_share: 'post', voice_media: 'voice', animated_media: 'gif', like: 'like', raven_media: 'photo', video_call_event: 'call', location: 'location', profile: 'contact', xma_media_share: 'post', xma_clip: 'reel', xma_story_share: 'post' }
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36'
const APP_ID = '936619743392459'
const BASE = 'https://www.instagram.com/api/v1/'

export function cookieHeader(c) {
  return Object.entries(c ?? {}).filter(([, v]) => v).map(([k, v]) => `${k}=${v}`).join('; ')
}

export function itemText(it) {
  if (it.item_type === 'text') return it.text ?? ''
  if (it.item_type === 'link') return it.link?.text ?? ''
  return placeholder(KIND[it.item_type] ?? 'post')
}

export function parseInbox(body, me) {
  const out = []
  for (const t of body?.inbox?.threads ?? []) {
    const users = new Map((t.users ?? []).map((u) => [String(u.pk ?? u.pk_id), u.full_name || u.username]))
    const name = t.thread_title || [...users.values()].join(', ') || 'Instagram'
    for (const it of (t.items ?? []).slice(0, 10)) {
      const text = itemText(it)
      if (!text) continue
      const fromMe = String(it.user_id) === String(me)
      out.push({ chat: { id: String(t.thread_id), name, isGroup: !!t.is_group }, msg: { id: String(it.item_id), fromMe, sender: t.is_group && !fromMe ? users.get(String(it.user_id)) ?? null : null, text, ts: Math.floor(Number(it.timestamp) / 1e6) } })
    }
  }
  return out
}

export function createInstagram({ authDir, store, emit }) {
  const base = createAdapterBase('instagram', { authDir, emit })
  let cookies = null
  let timer = null
  let firstPoll = true

  async function api(path, form) {
    const headers = { 'User-Agent': UA, 'X-IG-App-ID': APP_ID, 'X-Requested-With': 'XMLHttpRequest', 'X-CSRFToken': cookies.csrftoken ?? '', Referer: 'https://www.instagram.com/direct/inbox/', Origin: 'https://www.instagram.com', Cookie: cookieHeader(cookies), Accept: '*/*' }
    const init = { headers, redirect: 'manual' }
    if (form) {
      init.method = 'POST'
      headers['Content-Type'] = 'application/x-www-form-urlencoded'
      init.body = new URLSearchParams(form).toString()
    }
    const res = await fetch(BASE + path, init)
    const text = await res.text()
    let body = null
    try { body = JSON.parse(text) } catch {}
    if (res.status === 401 || res.status === 302 || body?.message === 'login_required' || body?.require_login || (!body && text.includes('not-logged-in'))) {
      const e = new Error('login_required')
      e.login = true
      throw e
    }
    if (!res.ok || body?.status === 'fail') throw new Error(body?.message || `Instagram respondió ${res.status}`)
    return body
  }

  async function poll() {
    const body = await api('direct_v2/inbox/?persistentBadging=true&folder=&limit=20&thread_message_limit=10')
    let changed = false
    for (const { chat, msg } of parseInbox(body, cookies.ds_user_id)) {
      const isNew = store.upsert('instagram', chat, msg)
      if (isNew) changed = true
      if (isNew && !firstPoll && !msg.fromMe) emit('im.message', { svc: 'instagram', chatId: chat.id, chatName: chat.name, isGroup: chat.isGroup, ...msg })
    }
    firstPoll = false
    if (changed) emit('im.changed', { svc: 'instagram' })
  }

  function startPolling() {
    clearInterval(timer)
    const tick = () => poll().catch((e) => {
      if (e.login) {
        clearInterval(timer)
        base.forget()
        base.set({ state: 'error', error: 'La sesión de Instagram se cerró: vuelve a entrar.' })
      }
    })
    timer = setInterval(tick, 30000 + Math.floor(Math.random() * 10000))
  }

  async function open(c) {
    cookies = c
    base.set({ state: 'connecting' })
    try {
      await poll()
    } catch (e) {
      cookies = null
      if (e.login) base.forget()
      base.set({ state: 'error', error: e.login ? 'Instagram no aceptó la sesión: vuelve a entrar.' : e.message })
      return base.status
    }
    base.save({ cookies: c })
    base.set({ state: 'open', me: c.username ? '@' + c.username : 'Instagram' })
    startPolling()
    return base.status
  }

  return {
    svc: 'instagram',
    get status() { return base.status },
    autostart: () => {
      const saved = base.load()
      return saved?.cookies?.sessionid ? open(saved.cookies).catch(() => {}) : null
    },
    async login(p = {}) {
      if (p.cookies?.sessionid) return open(p.cookies)
      base.set({ state: 'need', need: 'web' })
      return base.status
    },
    submit: () => { throw new RpcError('bad_request', 'Nada pendiente') },
    async logout() {
      clearInterval(timer)
      cookies = null
      base.forget()
      store.clear('instagram')
      base.set({ state: 'off' })
    },
    async send(chatId, text) {
      base.requireOpen()
      const ctx = randomUUID()
      await api('direct_v2/threads/broadcast/text/', { action: 'send_item', thread_ids: `[${chatId}]`, text, client_context: ctx, mutation_token: ctx, offline_threading_id: ctx })
      store.upsert('instagram', { id: chatId }, { id: 'local-' + ctx, fromMe: true, text, ts: Math.floor(Date.now() / 1000) })
      setTimeout(() => poll().catch(() => {}), 2500)
      return {}
    },
    async stickers() { return [] },
    async sendSticker() { throw new RpcError('unsupported', 'Instagram no deja mandar stickers desde fuera de su app') },
    callUrl: (chatId) => `https://www.instagram.com/direct/t/${chatId}/`,
  }
}
