import { useEffect, useState } from 'react'

/**
 * Returns a debounced copy of `value` that only updates after `delay` ms of
 * quiescence. POS-7: the customer lookup fired a request on every keystroke;
 * debouncing the search term (~300ms) collapses a burst of typing into a single
 * query while keeping the input itself fully responsive.
 */
export function useDebounce<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(handle)
  }, [value, delay])

  return debounced
}
