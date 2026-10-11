import { QueryClient, useQuery } from "@tanstack/react-query"
import { z } from "zod"
import { create } from "zustand"

import { ApiError, jsonBody, request, requestAs } from "./client"
import {
  optionsSchema,
  saveImageResultSchema,
  targetSetDetailSchema,
  targetSetJsonSchema,
  targetSetSummarySchema,
  windowSchema,
  type Options,
  type TargetSetJson,
  type WindowInfo,
} from "./schemas"

// A desktop tool talking to an in-process server: data only changes when the user
// acts, so nothing refetches on its own (no focus refetch, no staleness timer).
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: Infinity, refetchOnWindowFocus: false, retry: false },
  },
})

const enc = encodeURIComponent

const keys = {
  targets: ["targets"] as const,
  targetDetail: (name: string) => ["targets", name] as const,
  // Deliberately outside the "targets" prefix: the edit model backs an unsaved
  // draft and must not be refetched when a write invalidates the lists.
  targetEdit: (name: string) => ["target-edit", name] as const,
  windows: ["windows"] as const,
  options: ["options"] as const,
}

export function useTargetSets() {
  return useQuery({
    queryKey: keys.targets,
    queryFn: () => requestAs(z.array(targetSetSummarySchema), "/api/targets"),
  })
}

export function useTargetDetail(name: string) {
  return useQuery({
    queryKey: keys.targetDetail(name),
    queryFn: () =>
      requestAs(targetSetDetailSchema, `/api/targets/${enc(name)}`),
    enabled: name !== "",
  })
}

export function useTargetEdit(name: string) {
  return useQuery({
    queryKey: keys.targetEdit(name),
    queryFn: () =>
      requestAs(targetSetJsonSchema, `/api/targets/${enc(name)}/edit`),
    enabled: name !== "",
    gcTime: 0,
  })
}

export function useWindows() {
  return useQuery({
    queryKey: keys.windows,
    queryFn: () => requestAs(z.array(windowSchema), "/api/windows"),
  })
}

export function useOptions() {
  return useQuery({
    queryKey: keys.options,
    queryFn: () => requestAs(optionsSchema, "/api/options"),
  })
}

/** Call after any write to a target set; refreshes the lists and details on both tabs. */
export function invalidateTargets() {
  return queryClient.invalidateQueries({ queryKey: keys.targets })
}

export function saveOptions(options: Options) {
  return request("/api/options", jsonBody("PUT", options))
}

/** Waits `seconds`, then returns the foreground window (null when there is none). */
export function pickWindow(seconds: number): Promise<WindowInfo | null> {
  return requestAs(
    windowSchema.nullable(),
    "/api/windows/pick",
    jsonBody("POST", { seconds })
  )
}

export function startEngine(req: {
  windowHandles: string[]
  targetSet: string
  options: Options
}) {
  return request("/api/engine/start", jsonBody("POST", req))
}

export const stopEngine = () => request("/api/engine/stop", { method: "POST" })
export const pauseEngine = () =>
  request("/api/engine/pause", { method: "POST" })
export const resumeEngine = () =>
  request("/api/engine/resume", { method: "POST" })

export function createTargetSet(name: string) {
  return request("/api/targets/create", jsonBody("POST", { name }))
}

export function openTargetFolder(name: string) {
  return request(`/api/targets/${enc(name)}/open-folder`, { method: "POST" })
}

export function saveTargetJson(name: string, model: TargetSetJson) {
  return request(`/api/targets/${enc(name)}`, jsonBody("PUT", model))
}

export function deleteTargetImage(name: string, file: string) {
  return request(`/api/targets/${enc(name)}/images/${enc(file)}`, {
    method: "DELETE",
  })
}

/** Screenshots a window's client area; resolves to the PNG. */
export async function captureWindow(windowHandle: string): Promise<Blob> {
  const res = await fetch("/api/capture", jsonBody("POST", { windowHandle }))
  if (!res.ok) {
    const data = (await res.json().catch(() => null)) as {
      error?: string
    } | null
    throw new ApiError(data?.error || res.statusText, res.status, data)
  }
  return res.blob()
}

export interface SaveImageRequest {
  file: string
  dataUrl: string
  /** Client-area size of the screenshot the template was cut from. */
  baseWidth: number
  baseHeight: number
  overwrite?: boolean
}

/**
 * Stores a template image. The backend never overwrites silently: when a template
 * of the same base name exists it answers 409, surfaced here as `{ conflict }`
 * so the caller can ask the user and retry with `overwrite`.
 */
export async function saveTargetImage(
  name: string,
  req: SaveImageRequest
): Promise<z.infer<typeof saveImageResultSchema> | { conflict: string }> {
  try {
    return await requestAs(
      saveImageResultSchema,
      `/api/targets/${enc(name)}/images`,
      jsonBody("POST", req)
    )
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      const conflict = (e.data as { conflict?: string } | null)?.conflict
      return { conflict: conflict || req.file }
    }
    throw e
  }
}

// Template URLs carry a version: overwriting a template keeps its URL, so the
// browser would keep showing the cached image until the version changes.
export const useImageVersion = create<{ version: number; bump: () => void }>(
  (set) => ({
    version: 0,
    bump: () => set((s) => ({ version: s.version + 1 })),
  })
)

export function imageUrl(set: string, file: string, version: number) {
  return `/api/targets/${enc(set)}/images/${enc(file)}?v=${version}`
}
