import { resolve } from "node:path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": resolve(import.meta.dirname, "./src"),
    },
  },
  build: {
    // dist/ is served by the host's Kestrel: SmartOnmyoji.Host.csproj copies it
    // next to the exe as wwwroot/. One bundle loaded from 127.0.0.1: splitting it would buy nothing.
    chunkSizeWarningLimit: 1000,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    // Backend for development: `dotnet run --project src/SmartOnmyoji.Host -- serve 8770`
    proxy: {
      "/api": "http://127.0.0.1:8770",
    },
  },
})
