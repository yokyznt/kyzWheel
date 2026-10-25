import { readFileSync, writeFileSync, existsSync, mkdirSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import QRCode from 'qrcode'
import { RpcError } from '../protocol.mjs'

export const qrPng = (text) => QRCode.toDataURL(text, { margin: 1, width: 280, color: { dark: '#0b0b10', light: '#ffffff' } })

export function createAdapterBase(svc, { authDir, emit }) {
  const file = join(authDir, `${svc}.json`)
  let status = { svc, state: 'off' }
  let waiter = null
  return {
    get status() { return status },
    set(patch) {
      status = { svc, ...patch }
      emit('im.status', status)
    },
    load() {
      try { return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null } catch { return null }
    },
    save(data) {
      mkdirSync(authDir, { recursive: true })
      writeFileSync(file, JSON.stringify(data))
    },
    forget() { rmSync(file, { force: true }) },
    ask(need, hint) {
      this.set({ state: 'need', need, hint: hint ?? null })
      return new Promise((resolve, reject) => { waiter = { resolve, reject } })
    },
    submit(value) {
      if (!waiter) throw new RpcError('bad_request', 'No hay nada pendiente')
      const w = waiter
      waiter = null
      w.resolve(value)
    },
    cancel() {
      if (waiter) waiter.reject(new Error('cancelado'))
      waiter = null
    },
    requireOpen() {
      if (status.state !== 'open') throw new RpcError('not_connected', `Conecta ${svc} primero`)
    },
  }
}

export const placeholder = (kind) => ({
  photo: '📷 Foto', video: '🎬 Video', voice: '🎤 Nota de voz', audio: '🎵 Audio', sticker: '🩷 Sticker',
  document: '📄 Documento', gif: '🎞 GIF', location: '📍 Ubicación', contact: '👤 Contacto', poll: '📊 Encuesta', like: '❤️', reel: '🎬 Reel', post: '🖼 Publicación', call: '📞 Llamada',
})[kind] ?? '·'

export async function download(url, path) {
  const res = await fetch(url)
  if (!res.ok) throw new Error('http ' + res.status)
  writeFileSync(path, Buffer.from(await res.arrayBuffer()))
  return path
}
