import { defineConfig } from 'vite';

export default defineConfig({
    build: { outDir: '../wwwroot', emptyOutDir: true },
    server: { proxy: { '/api': 'http://127.0.0.1:5178' } }
});
