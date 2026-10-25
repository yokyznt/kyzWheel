import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { createImStore } from '../src/imstore.mjs'

const tmp = () => join(mkdtempSync(join(tmpdir(), 'im-')), 's.json')

test('recent merges services newest first and reports new messages once', () => {
  const s = createImStore(tmp())
  assert.equal(s.upsert('telegram', { id: 1, name: 'Ana' }, { id: 'a', text: 'hola', ts: 10 }), true)
  assert.equal(s.upsert('telegram', { id: 1 }, { id: 'a', text: 'hola!', ts: 10 }), false)
  s.upsert('discord', { id: 'c9', name: 'Beto', isGroup: true }, { id: 'b', text: 'yo', ts: 20, sender: 'Beto' })
  const r = s.recent(10)
  assert.deepEqual(r.map((m) => [m.svc, m.text]), [['discord', 'yo'], ['telegram', 'hola!']])
  assert.equal(r[1].chatName, 'Ana')
  assert.deepEqual(s.recent(10, ['telegram']).map((m) => m.id), ['a'])
})

test('limits chats and messages, clear removes a service', () => {
  const s = createImStore(tmp(), { maxChats: 2, maxMessagesPerChat: 2 })
  for (let i = 0; i < 4; i++) s.upsert('instagram', { id: 't', name: 'T' }, { id: 'm' + i, ts: i })
  assert.equal(s.recent(10).length, 2)
  s.upsert('instagram', { id: 'u' }, { id: 'x', ts: 50 })
  s.upsert('telegram', { id: 'v' }, { id: 'y', ts: 60 })
  assert.deepEqual(s.recent(10).map((m) => m.chatId), ['v', 'u'])
  s.clear('telegram')
  assert.deepEqual(s.recent(10).map((m) => m.chatId), ['u'])
})

test('stickers dedupe newest first and persist', () => {
  const f = tmp()
  const s = createImStore(f, { debounceMs: 0 })
  s.addSticker('discord', { id: '1', path: 'a.png' })
  s.addSticker('discord', { id: '2', path: 'b.png' })
  s.addSticker('discord', { id: '1', path: 'a.png' })
  assert.deepEqual(s.stickers('discord').map((x) => x.id), ['1', '2'])
  s.flush()
  assert.deepEqual(createImStore(f).stickers('discord').map((x) => x.id), ['1', '2'])
})
