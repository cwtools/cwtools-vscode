
/** @type {import('@vscode/test-cli').TestConfig} */
module.exports = {
  vscode: 'stable',
  extensionDevelopmentPath: "release",
  // extensionTestsEnv: { NODE_ENV: 'test' },
  // extensionTestsPath: './release/bin/client/test/suite',
  files: './release/bin/client/test/suite/**/*.test.js',
  launchArgs: [
    // Sample workspace the tests expect
    './client/test/sample',
    // Bring the file under test into the workspace
    './client/test/sample/events/irm.txt'
  ]
  // workplaceFolder: "D:\Synced\Git\Personal\cwtools-vscode\client\test\sample mod"
}
