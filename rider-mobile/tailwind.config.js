/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './app/**/*.{js,jsx,ts,tsx}',
    './src/**/*.{js,jsx,ts,tsx}',
  ],
  presets: [require('nativewind/preset')],
  theme: {
    extend: {
      colors: {
        // Rider brand: green (operational / field) vs customer blue
        brand: {
          50:  '#F0FDF4',
          100: '#DCFCE7',
          200: '#BBF7D0',
          300: '#86EFAC',
          400: '#4ADE80',
          500: '#22C55E',
          600: '#16A34A',
          700: '#15803D',
          800: '#166534',
          900: '#14532D',
        },
        surface: {
          DEFAULT: '#FFFFFF',
          muted:   '#F9FAFB',
          subtle:  '#F3F4F6',
        },
        text: {
          primary:   '#111827',
          secondary: '#6B7280',
          disabled:  '#9CA3AF',
          inverse:   '#FFFFFF',
        },
        success: '#16A34A',
        warning: '#D97706',
        danger:  '#DC2626',
        info:    '#0284C7',
        // Assignment status colours
        status: {
          active:    '#16A34A',
          on_break:  '#D97706',
          completed: '#0284C7',
          cancelled: '#DC2626',
          pending:   '#9CA3AF',
        },
      },
      fontFamily: {
        sans:   ['Inter', 'System'],
        medium: ['Inter-Medium', 'System'],
        bold:   ['Inter-Bold', 'System'],
      },
    },
  },
  plugins: [],
};
