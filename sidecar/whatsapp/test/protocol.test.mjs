import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createDispatcher, encodeEvent, RpcError } from '../src/protocol.mjs'

const d = createDispatcher({
  echo: async (p) => p,
  boom: async () => { throw new Error('se rompió') },
  denied: async () => { throw new RpcError('not_connected', 'Vincula WhatsApp primero') },
})

test('invalid line gives bad_request', async () => {
  const r = JSON.parse(await d('{nope'))
  assert.equal(r.id, null)
  assert.equal(r.error.code, 'bad_request')
})

test('missing id gives bad_request', async () => {
  assert.equal(JSON.parse(await d('{"method":"echo"}')).error.code, 'bad_request')
})

test('unknown method', async () => {
  const r = JSON.parse(await d('{"id":4,"method":"nada"}'))
  assert.deepEqual(r, { id: 4, error: { code: 'unknown_method', message: 'Método desconocido: nada' } })
})

test('handler throwing gives internal with message', async () => {
  assert.deepEqual(JSON.parse(await d('{"id":2,"method":"boom"}')), { id: 2, error: { code: 'internal', message: 'se rompió' } })
})

test('rpc error keeps its code', async () => {
  assert.equal(JSON.parse(await d('{"id":3,"method":"denied"}')).error.code, 'not_connected')
})

test('result keeps id and params default to object', async () => {
  assert.deepEqual(JSON.parse(await d('{"id":7,"method":"echo","params":{"a":1}}')), { id: 7, result: { a: 1 } })
  assert.deepEqual(JSON.parse(await d('{"id":8,"method":"echo"}')), { id: 8, result: {} })
})

test('event encoding', () => {
  assert.equal(encodeEvent('qr', { png: 'x' }), '{"event":"qr","data":{"png":"x"}}')
})
