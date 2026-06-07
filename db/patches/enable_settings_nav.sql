-- Surface the Settings module in the data-driven sidebar.
-- The 'settings' module already exists (seeded by seed_navigator_modules.sql) but
-- was hidden (show_in_nav=false, no route). Point it at /settings and show it.
-- Idempotent.
UPDATE identity_access.modules
SET route        = '/settings',
    icon         = 'Settings',
    section      = 'Administration',
    nav_order    = 50,
    show_in_nav  = true
WHERE key = 'settings';
