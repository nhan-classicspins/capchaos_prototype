/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      colors: {
        // the game's own ground (DesignTokens.Ground / SlotBand / SlotEmpty) for the previews
        board: { ground: '#4D5680', top: '#5A6090', band: '#474F7A', slot: '#3E4E78', locked: '#353C62', rail: '#25272E' },
      },
    },
  },
  plugins: [],
};
