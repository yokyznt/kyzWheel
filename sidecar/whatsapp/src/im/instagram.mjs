import { IgApiClient, IgLoginTwoFactorRequiredError, IgCheckpointError } from 'instagram-private-api'
import { createAdapterBase, placeholder } from './common.mjs'
import { RpcError } from '../protocol.mjs'

const KIND = { media: 'photo', media_share: 'post', clip: 'reel', reel_share: 'reel', story_share: 'post', voice_media: 'voice', animated_media: 'gif', like: 'like', raven_media: 'photo', video_call_event: 'call', location: 'location', profile: 'contact' }

export function createInstagram({ authDir, store, emit }) {
  const base = createAdapterBase('instagram', { authDir, emit })
  let ig = null
  let me = null
  let timer = null
  let firstPoll = true

  async function persist(username) {
    const state = await ig.state.serialize()
    delete state.constants
    base.save({ username, state })
  }

  function itemText(it) {
    if (it.item_type === 'text') return it.text ?? ''
    if (it.item_type === 'link') return it.link?.text ?? ''
    return placeholder(KIND[it.item_type] ?? 'post')
  }

  async function poll() {
    const threads = await ig.feed.directInbox().items()
    let changed = false
    for (const t of threads) {
      const users = new Map((t.users ?? []).map((u) => [String(u.pk), u.full_name || u.username]))
      const name = t.thread_title || [...users.values()].join(', ') || 'Instagram'
      for (const it of (t.items ?? []).slice(0, 10)) {
        const fromMe = String(it.user_id) === String(me)
        const text = itemText(it)
        if (!text) continue
        const ts = Math.floor(Number(it.timestamp) / 1e6)
        const sender = t.is_group && !fromMe ? users.get(String(it.user_id)) ?? null : null
        const isNew = store.upsert('instagram', { id: t.thread_id, name, isGroup: !!t.is_group }, { id: it.item_id, fromMe, sender, text, ts })
        if (isNew) changed = true
        if (isNew && !firstPoll && !fromMe) emit('im.message', { svc: 'instagram', chatId: t.thread_id, chatName: name, isGroup: !!t.is_group, id: it.item_id, sender, text, ts })
      }
    }
    firstPoll = false
    if (changed) emit('im.changed', { svc: 'instagram' })
  }

  function startPolling() {
    clearInterval(timer)
    const tick = () => poll().catch((e) => {
      if (/login_required|challenge/i.test(e?.message ?? '')) {
        clearInterval(timer)
        base.set({ state: 'error', error: 'Instagram pidió iniciar sesión otra vez' })
      }
    })
    tick()
    timer = setInterval(tick, 30000 + Math.floor(Math.random() * 10000))
  }

  async function opened(username) {
    me = ig.state.cookieUserId
    await persist(username)
    base.set({ state: 'open', me: '@' + username })
    startPolling()
  }

  async function login({ username, password } = {}) {
    username = String(username ?? '').trim().replace(/^@/, '')
    if (!username || !password) {
      base.set({ state: 'need', need: 'credentials' })
      return base.status
    }
    ig = new IgApiClient()
    ig.state.generateDevice(username)
    base.set({ state: 'connecting' })
    try {
      await ig.account.login(username, password)
      await opened(username)
    } catch (e) {
      if (e instanceof IgLoginTwoFactorRequiredError) {
        const info = e.response.body.two_factor_info
        const method = info.totp_two_factor_on ? '0' : '1'
        const code = await base.ask('code', method === '0' ? 'Código de tu app de autenticación' : 'Código que te llegó por SMS')
        await ig.account.twoFactorLogin({ username, verificationCode: String(code).trim(), twoFactorIdentifier: info.two_factor_identifier, verificationMethod: method, trustThisDevice: '1' })
        await opened(username)
      } else if (e instanceof IgCheckpointError) {
        await ig.challenge.auto(true)
        const code = await base.ask('code', 'Instagram te mandó un código de seguridad por correo o SMS')
        await ig.challenge.sendSecurityCode(String(code).trim())
        await opened(username)
      } else {
        base.set({ state: 'error', error: e?.response?.body?.message || e?.message || String(e) })
      }
    }
    return base.status
  }

  async function restore() {
    const saved = base.load()
    if (!saved?.state) return
    ig = new IgApiClient()
    ig.state.generateDevice(saved.username)
    await ig.state.deserialize(saved.state)
    base.set({ state: 'connecting' })
    try {
      await ig.account.currentUser()
      await opened(saved.username)
    } catch {
      base.set({ state: 'error', error: 'La sesión de Instagram expiró: vuelve a entrar.' })
    }
  }

  return {
    svc: 'instagram',
    get status() { return base.status },
    autostart: () => restore().catch(() => {}),
    login: (p) => login(p).catch((e) => base.set({ state: 'error', error: e?.message ?? String(e) })).then(() => base.status),
    submit: (v) => base.submit(v),
    async logout() {
      clearInterval(timer)
      base.cancel()
      try { await ig?.account.logout() } catch {}
      ig = null
      base.forget()
      store.clear('instagram')
      base.set({ state: 'off' })
    },
    async send(chatId, text) {
      base.requireOpen()
      await ig.entity.directThread(chatId).broadcastText(text)
      store.upsert('instagram', { id: chatId }, { id: 'local-' + Date.now(), fromMe: true, text, ts: Math.floor(Date.now() / 1000) })
      setTimeout(() => poll().catch(() => {}), 2500)
      return {}
    },
    async stickers() { return [] },
    async sendSticker() { throw new RpcError('unsupported', 'Instagram no deja mandar stickers desde fuera de su app') },
    callUrl: (chatId) => `https://www.instagram.com/direct/t/${chatId}/`,
  }
}
