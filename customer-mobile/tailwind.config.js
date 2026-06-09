/** @type {import('tailwindcss').Config} */
//
// Laundry Ghar — Customer "v2" design system.
// Warm cream canvas, olive/forest-green primary, gold/amber CTA accent.
// Mirrors the rider-mobile Partner-v2 palette so the two apps share a look.
//
module.exports = {
  content: [
    './app/**/*.{js,jsx,ts,tsx}',
    './src/**/*.{js,jsx,ts,tsx}',
  ],
  presets: [require('nativewind/preset')],
  theme: {
    extend: {
      colors: {
        // Warm cream — primary background canvas
        cream: {
          DEFAULT: '#F3EEE3',
          50:  '#FAF7F0',
          100: '#F3EEE3',
          200: '#ECE5D7',
          300: '#E0D8C6',
          400: '#D2C8B2',
        },
        // Olive / forest green — primary brand colour (headers, confirms)
        olive: {
          50:  '#F1F3E8',
          100: '#E3E7D0',
          200: '#CBD2AC',
          300: '#AEB983',
          400: '#909C5C',
          500: '#73803F',
          600: '#5C6A33',
          700: '#4A552A',
          800: '#3B4423',
          900: '#2E351C',
        },
        // Gold / amber — primary CTA accent
        gold: {
          50:  '#FBF4E0',
          100: '#F7E8BF',
          200: '#EFD68F',
          300: '#E6C260',
          400: '#DBAC3D',
          500: '#CC9A2C',
          600: '#AE8123',
          700: '#8A641D',
        },
        // Ink — text colours
        ink: {
          DEFAULT: '#1E2119',
          soft:    '#3C3F35',
          muted:   '#7B7A6C',
          faint:   '#A8A493',
        },
        // Surface — card / container backgrounds
        surface: {
          DEFAULT: '#FFFFFF',
          card:    '#FBF9F3',
          muted:   '#F3EEE3',
          subtle:  '#ECE5D7',
        },
        // Text scale (kept for components that read `text-*`)
        text: {
          primary:   '#1E2119',
          secondary: '#7B7A6C',
          disabled:  '#A8A493',
          inverse:   '#FFFFFF',
        },
        success: '#4F8A4F',
        warning: '#CC9A2C',
        danger:  '#C0492F',
        info:    '#3F6E8C',
      },
      fontFamily: {
        sans:   ['System'],
        medium: ['System'],
        bold:   ['System'],
      },
    },
  },
  plugins: [],
};
