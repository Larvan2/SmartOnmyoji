import { toast } from "@/components/ui/toast"

const TIMEOUT_MS = 3200

function show(type: "success" | "error" | "info", title: string) {
  toast.add({ type, title, timeout: TIMEOUT_MS })
}

export const notify = {
  ok: (title: string) => show("success", title),
  error: (title: string) => show("error", title),
  info: (title: string) => show("info", title),
}
