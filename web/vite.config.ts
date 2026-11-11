import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Em desenvolvimento, /api vai para a API .NET (porta 5080).
// No publish, o build (dist) é servido pela própria API.
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
    proxy: { '/api': 'http://127.0.0.1:5080' },
  },
  build: { outDir: '../src/JetPoa.Api/wwwroot', emptyOutDir: true },
})
