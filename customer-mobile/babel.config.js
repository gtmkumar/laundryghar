module.exports = function (api) {
  api.cache(true);
  return {
    presets: [
      ['babel-preset-expo', { jsxImportSource: 'nativewind' }],
      'nativewind/babel',
    ],
    plugins: [
      [
        'module-resolver',
        {
          root: ['.'],
          alias: {
            '@': './src',
          },
        },
      ],
      // NOTE: react-native-reanimated/plugin intentionally omitted — the app uses
      // only GestureHandlerRootView (no Reanimated worklets), and SDK 52 has no
      // react-native-worklets the 3.16 plugin can resolve. Re-add (last) with a
      // proper worklets setup when building animated screens.
    ],
  };
};
