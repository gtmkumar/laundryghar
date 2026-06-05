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
        brand: {
          50:  '#EFF6FF',
          100: '#DBEAFE',
          200: '#BFDBFE',
          300: '#93C5FD',
          400: '#60A5FA',
          500: '#3B82F6',
          600: '#2563EB',
          700: '#1D4ED8',
          800: '#1E40AF',
          900: '#1E3A8A',
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
