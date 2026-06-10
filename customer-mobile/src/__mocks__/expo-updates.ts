/** Minimal expo-updates mock — prevents native module errors during unit tests. */
export const checkForUpdateAsync = jest.fn(async () => ({ isAvailable: false }));
export const fetchUpdateAsync = jest.fn(async () => ({}));
export const reloadAsync = jest.fn(async () => {});
export const isEmbeddedLaunch = true;
export const channel = 'test';
export const updateId = null;
