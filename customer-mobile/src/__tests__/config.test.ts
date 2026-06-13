import { CONFIG } from '@/constants/config';

/**
 * With no `extra` override and no Metro hostUri (the test mock supplies neither),
 * every service URL must default to the API Gateway on :8080 with its path prefix.
 * This guards against regressing back to direct service ports (5050/5002/5005).
 */
describe('CONFIG default service URLs (gateway routing)', () => {
  it.each([
    ['identityApiUrl', 'identity'],
    ['catalogApiUrl', 'catalog'],
    ['ordersApiUrl', 'orders'],
    ['commerceApiUrl', 'commerce'],
    ['engagementApiUrl', 'engagement'],
  ] as const)('%s routes through :8080/%s', (key, prefix) => {
    expect(CONFIG[key]).toBe(`http://localhost:8080/${prefix}`);
  });

  it('never points at a direct service port', () => {
    for (const key of [
      'identityApiUrl',
      'catalogApiUrl',
      'ordersApiUrl',
      'commerceApiUrl',
      'engagementApiUrl',
    ] as const) {
      expect(CONFIG[key]).toMatch(/:8080\//);
      expect(CONFIG[key]).not.toMatch(/:50[0-9]{2}(\/|$)/);
    }
  });
});
