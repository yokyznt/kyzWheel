export class RpcError extends Error {
  constructor(code, message) {
    super(message)
    this.code = code
  }
}

export function parseLine(line) {
  let msg
  try {
    msg = JSON.parse(line)
  } catch {
    throw new RpcError('bad_request', 'JSON inválido')
  }
  if (!msg || typeof msg !== 'object' || !Number.isInteger(msg.id) || typeof msg.method !== 'string')
    throw new RpcError('bad_request', 'Falta id o method')
  return { id: msg.id, method: msg.method, params: msg.params && typeof msg.params === 'object' ? msg.params : {} }
}

export const encodeResult = (id, result) => JSON.stringify({ id, result: result ?? null })

export const encodeError = (id, code, message) => JSON.stringify({ id, error: { code, message } })

export const encodeEvent = (event, data) => JSON.stringify({ event, data: data ?? {} })

export function createDispatcher(handlers) {
  return async (line) => {
    let req
    try {
      req = parseLine(line)
    } catch (e) {
      return encodeError(null, e.code ?? 'bad_request', e.message)
    }
    const fn = handlers[req.method]
    if (typeof fn !== 'function') return encodeError(req.id, 'unknown_method', `Método desconocido: ${req.method}`)
    try {
      return encodeResult(req.id, await fn(req.params))
    } catch (e) {
      return encodeError(req.id, e instanceof RpcError ? e.code : 'internal', e?.message ?? String(e))
    }
  }
}
