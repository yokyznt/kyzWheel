import { join } from 'node:path'
import { createImStore, SERVICES } from '../imstore.mjs'
import { createTelegram } from './telegram.mjs'
import { createDiscord } from './discord.mjs'
import { createInstagram } from './instagram.mjs'
import { RpcError } from '../protocol.mjs'

export function createMessengers({ authDir, dataDir, cacheDir, emit }) {
  const store = createImStore(join(dataDir, 'im-store.json'))
  const ctx = { authDir, cacheDir, store, emit }
  const all = { telegram: createTelegram(ctx), discord: createDiscord(ctx), instagram: createInstagram(ctx) }
  const get = (svc) => all[svc] ?? (() => { throw new RpcError('bad_request', 'Servicio desconocido: ' + svc) })()
  return {
    start() {
      for (const a of Object.values(all)) {
        try { a.autostart() } catch {}
      }
    },
    handlers: {
      'im.status': async () => Object.fromEntries(SERVICES.map((s) => [s, all[s].status])),
      'im.login': async ({ svc, ...p }) => get(svc).login(p),
      'im.submit': async ({ svc, value }) => get(svc).submit(value),
      'im.logout': async ({ svc }) => get(svc).logout(),
      'im.recent': async ({ limit = 120, services }) => store.recent(limit, services ?? SERVICES),
      'im.send': async ({ svc, chatId, text }) => {
        if (!text?.trim()) throw new RpcError('bad_request', 'Escribe algo')
        return get(svc).send(String(chatId), text)
      },
      'im.stickers': async ({ svc }) => get(svc).stickers(),
      'im.sticker.send': async ({ svc, chatId, id }) => get(svc).sendSticker(String(chatId), String(id)),
      'im.call': async ({ svc, chatId }) => ({ url: get(svc).callUrl(String(chatId)) }),
    },
    flush: () => store.flush(),
  }
}
