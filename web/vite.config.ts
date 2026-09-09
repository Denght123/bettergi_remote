import {defineConfig} from 'vite';

export default defineConfig({
  build: {
    target: 'es2022',
    sourcemap: true,
    cssCodeSplit: true,
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/ws': {target: 'ws://127.0.0.1:8080', ws: true},
      '/healthz': {target: 'http://127.0.0.1:8080'},
    },
  },
});
