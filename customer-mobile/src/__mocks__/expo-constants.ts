/**
 * Mock for expo-constants — lets versionGate tests control the current app version
 * without needing a native build. Default is "1.0.0"; individual tests can override.
 */
const Constants = {
  expoConfig: {
    version: '1.0.0',
  },
  // Minimal subset — add fields as tests require them.
  manifest: null,
};

export default Constants;
