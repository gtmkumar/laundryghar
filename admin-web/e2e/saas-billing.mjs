// Standalone Playwright E2E for the SaaS tier-billing flow (uses the `playwright` core lib — no
// test runner). Drives the running dev stack (admin-web :5174 + hosts) end to end:
//   login → Licensing (apply a tier) → platform-tier card + invoice → mark invoice paid →
//   Platform billing MRR page.
// Run (stack must be up):  node e2e/saas-billing.mjs
import { chromium } from 'playwright'
import { mkdirSync } from 'node:fs'

const BASE = process.env.E2E_BASE || 'http://localhost:5174'
const EMAIL = process.env.E2E_USER || 'admin@laundryghar.local'
const PASS = process.env.E2E_PASS || 'Admin@123'
const SHOTS = new URL('./screenshots/', import.meta.url).pathname
mkdirSync(SHOTS, { recursive: true })

const results = []
const ok = (name, cond) => { results.push({ name, ok: !!cond }); console.log(`${cond ? '✓' : '✗'} ${name}`) }
const shot = (n) => page.screenshot({ path: `${SHOTS}${n}.png`, fullPage: true }).catch(() => {})

const browser = await chromium.launch()
const ctx = await browser.newContext({ viewport: { width: 1440, height: 950 } })
const page = await ctx.newPage()

try {
  // 1) Login ----------------------------------------------------------------
  await page.goto(BASE, { waitUntil: 'networkidle' })
  await page.locator('#identifier').fill(EMAIL)
  await page.locator('#password').fill(PASS)
  await page.locator('form button[type="submit"]').click()
  await page.waitForURL((u) => !u.toString().includes('/login'), { timeout: 15000 })
  await page.waitForLoadState('networkidle')
  ok('logged in (password field gone)', !(await page.locator('#password').count()))
  await shot('01-after-login')

  // 2) Licensing tab → apply the Pro tier -----------------------------------
  await page.goto(`${BASE}/access-control?tab=modules`, { waitUntil: 'networkidle' })
  const planSelect = page.locator('select:has(option[value="pro"])')
  await planSelect.waitFor({ state: 'visible', timeout: 20000 })
  await planSelect.selectOption('pro')
  await page.getByRole('button', { name: 'Apply', exact: true }).click()

  // platform-tier card appears once the subscription exists
  await page.getByText('Platform tier', { exact: false }).first().waitFor({ state: 'visible', timeout: 20000 })
  const tierBody = await page.locator('body').innerText()
  ok('tier card shows Pro', /Platform tier[\s\S]*Pro/i.test(tierBody))
  ok('an invoice row + Mark paid action present', (await page.getByRole('button', { name: 'Mark paid' }).count()) > 0)
  await shot('02-licensing-applied')

  // 3) Mark the issued invoice paid -----------------------------------------
  await page.getByRole('button', { name: 'Mark paid' }).first().click()
  await page.waitForTimeout(1500) // let the mutation + refetch settle
  ok('an invoice now shows "paid"', /paid/i.test(await page.locator('body').innerText()))
  await shot('03-invoice-paid')

  // 4) Platform billing / MRR page ------------------------------------------
  await page.goto(`${BASE}/platform-billing`, { waitUntil: 'networkidle' })
  await page.getByText('Monthly recurring revenue').first().waitFor({ state: 'visible', timeout: 20000 })
  const mrrBody = await page.locator('body').innerText()
  ok('MRR card present', /Monthly recurring revenue/i.test(mrrBody))
  ok('Active tenants card present', /Active tenants/i.test(mrrBody))
  ok('a ₹ amount is rendered', /₹\s?[\d,]/.test(mrrBody))
  ok('revenue-by-tier shows Enterprise or Pro', /(Enterprise|Pro)/.test(mrrBody))
  await shot('04-platform-billing')
} catch (e) {
  console.error('E2E error:', e.message)
  results.push({ name: `ran without throwing (${e.message})`, ok: false })
  await shot('99-error')
} finally {
  await browser.close()
}

const failed = results.filter((r) => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} checks passed`)
process.exit(failed.length ? 1 : 0)
