/** In-memory AsyncStorage mock — no native module required. */
const store: Record<string, string> = {};

const AsyncStorage = {
  getItem:    jest.fn(async (key: string) => store[key] ?? null),
  setItem:    jest.fn(async (key: string, value: string) => { store[key] = value; }),
  removeItem: jest.fn(async (key: string) => { delete store[key]; }),
  clear:      jest.fn(async () => { Object.keys(store).forEach(k => delete store[k]); }),
  getAllKeys:  jest.fn(async () => Object.keys(store)),
  multiGet:   jest.fn(async (keys: string[]) => keys.map(k => [k, store[k] ?? null])),
};

export default AsyncStorage;
