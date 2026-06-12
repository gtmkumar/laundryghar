/**
 * Typography constants — NativeWind className strings for consistent type scale.
 *
 * Use these instead of scattering ad-hoc font-size / font-weight combinations.
 * Apply via spread: <Text className={ty.title}>…</Text>
 *
 * Scale:
 *   screenTitle   — top-level screen headings (e.g. "Your orders")
 *   sectionTitle  — section headings within a screen
 *   cardTitle     — card / item primary label
 *   bodyBase      — default body copy
 *   bodySmall     — secondary body copy, subtitles
 *   caption       — metadata, dates, hints
 *   label         — eyebrow labels (uppercase + tracking)
 */
export const ty = {
  /** Screen-level heading — 2xl extrabold, ink */
  screenTitle: 'text-2xl font-extrabold text-ink',
  /** Section heading — lg extrabold, ink */
  sectionTitle: 'text-lg font-extrabold text-ink',
  /** Card / item primary label — base bold, ink */
  cardTitle: 'text-base font-bold text-ink',
  /** Default body copy — sm, ink-muted */
  bodyBase: 'text-sm text-ink-muted',
  /** Secondary body — xs, ink-muted */
  bodySmall: 'text-xs text-ink-muted',
  /** Metadata / dates / hints — xs, ink-faint */
  caption: 'text-xs text-ink-faint',
  /** Eyebrow / section label — [11px] bold uppercase tracking-wider, ink-faint */
  label: 'text-[11px] font-bold uppercase tracking-wider text-ink-faint',
} as const;
