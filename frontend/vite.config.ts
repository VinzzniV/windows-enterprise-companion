import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// Production assets go straight into the host's wwwroot (served via
// WebView2 virtual host mapping); DEBUG can point at the dev server instead.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: './',
  build: {
    outDir: '../src/Wec.Host/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
  },
});
