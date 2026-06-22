import { useState } from 'react'
import { slugifyCode } from '@/lib/utils'

/**
 * Auto-suggests a Code/SKU from the Name field. As long as the user hasn't
 * manually edited the Code, typing the Name keeps the Code in sync (slugified
 * + uppercased). The moment the user edits the Code directly, auto-sync stops.
 *
 * Usage in a create/edit drawer:
 *   const codeF = useAutoCode()
 *   // Name input:  onChange={(e) => { setName(e.target.value); codeF.syncFromName(e.target.value) }}
 *   // Code input:  value={codeF.code} onChange={(e) => codeF.setCode(e.target.value)}
 *   // On seed:     codeF.seed(item?.code ?? '', isEdit)   // edit locks the code
 */
export function useAutoCode() {
  const [code, setCodeState] = useState('')
  const [touched, setTouched] = useState(false)

  return {
    code,
    /** The user typed in the Code field directly — stop auto-syncing. */
    setCode: (v: string) => { setTouched(true); setCodeState(v) },
    /** Call from the Name field's onChange — fills the Code until it's been edited. */
    syncFromName: (name: string) => { if (!touched) setCodeState(slugifyCode(name)) },
    /** Seed on open. `locked` (edit mode) prevents name-driven overwrites. */
    seed: (value: string, locked: boolean) => { setCodeState(value); setTouched(locked) },
  }
}
