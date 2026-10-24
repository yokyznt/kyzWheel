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
  assert.equal(textOf({ audioMessage: {} }), '🎤 Audio')
  assert.equal(textOf({ documentMessage: { fileName: 'a.pdf' } }), '📄 a.pdf')
})
