import { CONFIG } from '@/constants/config';

/**
 * With no `extra` override and no Metro hostUri (the test mock supplies neither),
 * every service URL must default to the API Gateway with its path prefix.
 * This guards against regressing back to direct per-service ports.
 *
 * The port is the AppHost's reserved gateway endpoint (5300), not 8080 — see the note in
 * constants/config.ts for why 8080 is unusable as a host port on a dev machine.
 */
describe('CONFIG default service URLs (gateway routing)', () => {
  it.each([
    ['identityApiUrl', 'identity'],
    ['catalogApiUrl', 'catalog'],
    ['ordersApiUrl', 'orders'],
    ['commerceApiUrl', 'commerce'],
    ['engagementApiUrl', 'engagement'],
  ] as const)('%s routes through the gateway /%s', (key, prefix) => {
    expect(CONFIG[key]).toBe(`http://localhost:5300/${prefix}`);
  });

  it('never points at a direct service port', () => {
    for (const key of [
      'identityApiUrl',
      'catalogApiUrl',
      'ordersApiUrl',
      'commerceApiUrl',
      'engagementApiUrl',
    ] as const) {
      expect(CONFIG[key]).toMatch(/:5300\//);
      // never a DIRECT service port — 5301-5303 are behind the gateway, not addressed from here
      expect(CONFIG[key]).not.toMatch(/:(50[0-9]{2}|530[1-9])(\/|$)/);
    }
  });
});
