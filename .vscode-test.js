
/** @type {import('@vscode/test-cli').IBaseTestConfiguration} */
module.exports = {
  vscode: 'stable',
  extensionDevelopmentPath: "release",
  files: './release/bin/client/test/suite/**/*.test.js',
  workspaceFolder: "./release/bin/client/test/Stellaris/sample",
  launchArgs: [
    // Bring the file under test into the workspace
    './client/test/Stellaris/sample/events/irm.txt'
  ]
}
