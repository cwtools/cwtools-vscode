
import { defineConfig } from '@vscode/test-cli';
export default defineConfig([
  {
    // Required: Glob of files to load (can be an array and include absolute paths).
    files: 'out/client/test/**/*.test.js',
    // Optional: Root path of your extension, same as the API above, defaults
    // to the directory this config file is in
    extensionDevelopmentPath: "release",
    launchArgs: [
    // Sample workspace the tests expect
    './client/test/sample mod',
    // Bring the file under test into the workspace
    './client/test/sample mod/events/irm.txt'
    ]
    // Optional: additional mocha options to use:
    // mocha: {
      // require: `./out/test-utils.js`,
      // timeout: 20000,
    // },
  },
  // you can specify additional test configurations if necessary
]);
