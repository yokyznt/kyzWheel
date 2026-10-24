import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { createStore, textOf } from '../src/store.mjs'

const msg = (jid, id, ts, message, extra = {}) => ({ key: { remoteJid: jid, id, fromMe: false }, messageTimestamp: ts, message, ...extra })

test('recent chats sorted by lastTs desc and named from contacts', () => {
  const s = createStore(null)
  s.upsertContacts([{ id: 'a@s.whatsapp.net', notify: 'Ana' }])
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 100, { conversation: 'hola' }), msg('b@s.whatsapp.net', '2', 200, { conversation: 'yo' })])
  const r = s.recentChats(8)
  assert.deepEqual(r.map((c) => c.jid), ['b@s.whatsapp.net', 'a@s.whatsapp.net'])
  assert.equal(r[1].name, 'Ana')
  assert.equal(r[1].lastText, 'hola')
})

test('respects limits', () => {
  const s = createStore(null, { maxChats: 2, maxMessagesPerChat: 3 })
  for (let i = 0; i < 5; i++) s.upsertMessages([msg('a@s.whatsapp.net', 'm' + i, i, { conversation: 'x' + i })])
  assert.deepEqual(s.messages('a@s.whatsapp.net').map((m) => m.id), ['m2', 'm3', 'm4'])
  s.upsertMessages([msg('b@s.whatsapp.net', 'b', 10, { conversation: 'b' }), msg('c@s.whatsapp.net', 'c', 11, { conversation: 'c' })])
  assert.equal(s.recentChats(8).length, 2)
})

test('recent files only media and documents', () => {
  const s = createStore(null)
  s.upsertMessages([
    msg('a@s.whatsapp.net', '1', 1, { conversation: 'solo texto' }),
    msg('a@s.whatsapp.net', '2', 2, { documentMessage: { fileName: 'tarea.pdf', mimetype: 'application/pdf', fileLength: 1200 } }),
    msg('a@s.whatsapp.net', '3', 3, { imageMessage: { mimetype: 'image/jpeg', caption: 'foto' } }),
  ])
  const f = s.recentFiles(8)
  assert.deepEqual(f.map((x) => x.messageId), ['3', '2'])
  assert.equal(f[1].fileName, 'tarea.pdf')
  assert.equal(f[1].size, 1200)
})

test('contact search ignores accents and case', () => {
  const s = createStore(null)
  s.upsertContacts([{ id: 'j@s.whatsapp.net', name: 'José Pérez' }, { id: 'm@s.whatsapp.net', name: 'María' }])
  assert.deepEqual(s.searchContacts('jose').map((c) => c.name), ['José Pérez'])
  assert.deepEqual(s.searchContacts('MAR').map((c) => c.name), ['María'])
})

test('unread counts only new incoming', () => {
  const s = createStore(null)
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 1, { conversation: 'x' })], { countUnread: true })
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 1, { conversation: 'x' })], { countUnread: true })
  s.upsertMessages([{ key: { remoteJid: 'a@s.whatsapp.net', id: '2', fromMe: true }, messageTimestamp: 2, message: { conversation: 'yo' } }], { countUnread: true })
  assert.equal(s.unreadTotal(), 1)
  s.markRead('a@s.whatsapp.net')
  assert.equal(s.unreadTotal(), 0)
})

test('ignores status broadcast and empty messages', () => {
  const s = createStore(null)
  s.upsertMessages([msg('status@broadcast', '1', 1, { conversation: 'x' }), { key: { remoteJid: 'a@s.whatsapp.net', id: '2' }, messageTimestamp: 2 }])
  assert.equal(s.recentChats().length, 0)
})

test('persistence roundtrip', () => {
  const file = join(mkdtempSync(join(tmpdir(), 'kyzwa-')), 'store.json')
  const s = createStore(file, { debounceMs: 10 })
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 5, { conversation: 'guardado' })])
  s.flush()
  const back = createStore(file)
  assert.equal(back.recentChats()[0].lastText, 'guardado')
})

test('text of wrapped and media messages', () => {
  assert.equal(textOf({ ephemeralMessage: { message: { extendedTextMessage: { text: 'efímero' } } } }), 'efímero')
  assert.equal(textOf({ audioMessage: {} }), '🎵 Audio')
  assert.equal(textOf({ documentMessage: { fileName: 'a.pdf' } }), '📄 a.pdf')
})

test('lid chat gets saved contact name through alias', () => {
  const s = createStore(null)
  s.upsertContacts([{ id: '5215550001111@s.whatsapp.net', name: 'Mamá' }])
  s.upsertMessages([msg('123456789@lid', 'x1', 50, { conversation: 'hola hijo' }, { pushName: 'Lupita', key: { remoteJid: '123456789@lid', remoteJidAlt: '5215550001111@s.whatsapp.net', id: 'x1', fromMe: false } })])
  assert.equal(s.recentChats(1)[0].name, 'Mamá')
  assert.equal(s.recentChats(1)[0].phone, '+5215550001111')
})

test('falls back to push name then phone', () => {
  const s = createStore(null)
  s.upsertMessages([msg('5215550002222@s.whatsapp.net', 'p1', 10, { conversation: 'hey' }, { pushName: 'Beto' })])
  s.upsertMessages([msg('5215550003333@s.whatsapp.net', 'p2', 11, { conversation: 'yo' })])
  const names = Object.fromEntries(s.recentChats(5).map((c) => [c.jid, c.name]))
  assert.equal(names['5215550002222@s.whatsapp.net'], 'Beto')
  assert.equal(names['5215550003333@s.whatsapp.net'], '+5215550003333')
})

test('saved name is not overwritten by notify', () => {
  const s = createStore(null)
  s.upsertContacts([{ id: 'a@s.whatsapp.net', name: 'Ana Trabajo' }])
  s.upsertContacts([{ id: 'a@s.whatsapp.net', notify: 'ana🌸' }])
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 1, { conversation: 'x' })])
  assert.equal(s.recentChats(1)[0].name, 'Ana Trabajo')
})

test('group names and sender prefixes', () => {
  const s = createStore(null)
  s.setGroupNames({ 'g1@g.us': { subject: 'Familia' } })
  s.upsertContacts([{ id: 'p@s.whatsapp.net', name: 'Tío Juan' }])
  s.upsertMessages([{ key: { remoteJid: 'g1@g.us', id: 'g', fromMe: false, participant: 'p@s.whatsapp.net' }, messageTimestamp: 5, pushName: 'juanito', message: { conversation: 'qué onda' } }])
  const c = s.recentChats(1)[0]
  assert.equal(c.name, 'Familia')
  assert.equal(c.lastText, 'Tío: qué onda')
  assert.equal(s.messages('g1@g.us')[0].sender, 'Tío Juan')
})

test('media keeps kind, thumb and download keys but lean list hides keys', () => {
  const s = createStore(null)
  s.upsertMessages([msg('a@s.whatsapp.net', 'i', 1, { imageMessage: { mimetype: 'image/jpeg', jpegThumbnail: Buffer.from([1, 2, 3]), mediaKey: Buffer.from([9, 9]), directPath: '/v/t62/x', caption: 'mira' } })])
  const m = s.messages('a@s.whatsapp.net')[0]
  assert.equal(m.file.kind, 'image')
  assert.equal(m.file.thumb, 'AQID')
  assert.equal(m.caption, 'mira')
  assert.equal(m.file.media, undefined)
  assert.equal(s.findMessage('a@s.whatsapp.net', 'i').file.media.mediaKey, 'CQk=')
})

test('stickers, voice notes and locations are shown; reactions skipped', () => {
  const s = createStore(null)
  s.upsertMessages([
    msg('a@s.whatsapp.net', 's', 1, { stickerMessage: { mimetype: 'image/webp' } }),
    msg('a@s.whatsapp.net', 'v', 2, { audioMessage: { mimetype: 'audio/ogg; codecs=opus', ptt: true, seconds: 7 } }),
    msg('a@s.whatsapp.net', 'l', 3, { locationMessage: { degreesLatitude: 1 } }),
    msg('a@s.whatsapp.net', 'r', 4, { reactionMessage: { text: '❤' } }),
  ])
  const list = s.messages('a@s.whatsapp.net')
  assert.deepEqual(list.map((m) => m.id), ['s', 'v', 'l'])
  assert.equal(list[0].file.kind, 'sticker')
  assert.equal(list[1].file.ptt, true)
  assert.equal(list[1].file.seconds, 7)
  assert.equal(list[2].text, '📍 Ubicación')
})

test('recent messages across chats newest first with chat names', () => {
  const s = createStore(null)
  s.upsertContacts([{ id: 'a@s.whatsapp.net', name: 'Ana' }])
  s.setGroupNames({ 'g@g.us': { subject: 'Equipo' } })
  s.upsertMessages([msg('a@s.whatsapp.net', '1', 10, { conversation: 'viejo' }), msg('g@g.us', '2', 30, { conversation: 'nuevo' }), msg('a@s.whatsapp.net', '3', 20, { imageMessage: { mediaKey: Buffer.from([1]), directPath: '/x' } })])
  const r = s.recentMessages(2)
  assert.deepEqual(r.map((m) => m.id), ['2', '3'])
  assert.equal(r[0].chatName, 'Equipo')
  assert.equal(r[0].isGroup, true)
  assert.equal(r[1].chatName, 'Ana')
  assert.equal(r[1].file.media, undefined)
})
