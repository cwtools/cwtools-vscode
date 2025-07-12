/** @type {import('@vscode/test-cli').TestConfig} */
module.exports = {
  vscode: 'stable',
  extensionDevelopmentPath: "release",
  extensionTestsEnv: { NODE_ENV: 'test' },
  extensionTestsPath: './out/client/test/suite',
  files: './out/client/test/suite/**/*.test.js',
  launchArgs: [
    // Sample workspace the tests expect
    './client/test/sample mod',
    // Bring the file under test into the workspace
    './client/test/sample mod/events/irm.txt'
  ]
  // workplaceFolder: "D:\Synced\Git\Personal\cwtools-vscode\client\test\sample mod"
}
