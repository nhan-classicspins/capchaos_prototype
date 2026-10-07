import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { configApi } from './server/configApi';

// `npm run dev` serves the tool AND the /api the tool reads and writes the Unity config files through.
export default defineConfig({
  plugins: [react(), configApi()],
  server: { port: 5178 },
  preview: { port: 5178 },
});
