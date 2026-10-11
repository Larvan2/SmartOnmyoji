import type { z } from "zod"

export class ApiError extends Error {
  readonly status: number
  readonly data: unknown

  constructor(message: string, status: number, data: unknown) {
    super(message)
    this.status = status
    this.data = data
  }
}

/** Builds the RequestInit for a JSON-bodied request. */
export function jsonBody(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  }
}

/**
 * Calls the backend and returns the decoded JSON body (null when empty).
 * Non-2xx responses throw an ApiError carrying the body's `error` field as its message.
 */
export async function request(
  path: string,
  init?: RequestInit
): Promise<unknown> {
  const res = await fetch(path, init)
  const text = await res.text()
  let data: unknown = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    // non-JSON error body (e.g. a bare 404): fall through to statusText
  }
  if (!res.ok) {
    const message =
      (data as { error?: string } | null)?.error ||
      res.statusText ||
      `HTTP ${res.status}`
    throw new ApiError(message, res.status, data)
  }
  return data
}

/** Like {@link request}, but validates the response against `schema` so contract drift fails loudly. */
export async function requestAs<T>(
  schema: z.ZodType<T>,
  path: string,
  init?: RequestInit
): Promise<T> {
  return schema.parse(await request(path, init))
}

export function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}
