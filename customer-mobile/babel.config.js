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
      // Reanimated 4 (SDK 54) moved its Babel plugin to react-native-worklets.
      // Must be listed LAST. Required because react-native-reanimated now depends
      // on react-native-worklets even when the app uses only GestureHandlerRootView.
      'react-native-worklets/plugin',
    ],
  };
};
