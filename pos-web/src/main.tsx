import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { initI18n } from './i18n'

// Initialise i18n synchronously (browser localStorage read is sync) before
// rendering the root — the LanguageDetector resolves locale without async I/O.
initI18n()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
