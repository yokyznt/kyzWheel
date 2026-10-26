import { test } from 'node:test'
import assert from 'node:assert/strict'
import { parseInbox, cookieHeader } from '../src/im/instagram.mjs'

test('parses web inbox threads into chat messages', () => {
  const body = { inbox: { threads: [{ thread_id: '340', thread_title: '', is_group: true, users: [{ pk: 7, username: 'ana', full_name: 'Ana' }, { pk: 8, username: 'beto' }], items: [
    { item_id: 'a', user_id: 7, timestamp: '1700000000000000', item_type: 'text', text: 'hola' },
    { item_id: 'b', user_id: 99, timestamp: '1700000005000000', item_type: 'like' },
    { item_id: 'c', user_id: 8, timestamp: '1700000006000000', item_type: 'clip' },
  ] }] } }
  const r = parseInbox(body, 99)
  assert.equal(r.length, 3)
  assert.deepEqual(r[0].chat, { id: '340', name: 'Ana, beto', isGroup: true })
  assert.deepEqual(r[0].msg, { id: 'a', fromMe: false, sender: 'Ana', text: 'hola', ts: 1700000000 })
  assert.equal(r[1].msg.fromMe, true)
  assert.equal(r[1].msg.sender, null)
  assert.equal(r[2].msg.text, '🎬 Reel')
  assert.equal(cookieHeader({ sessionid: 's', csrftoken: 'c', x: '' }), 'sessionid=s; csrftoken=c')
})
