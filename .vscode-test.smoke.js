module.exports = {
  vscode: 'stable',
  extensionDevelopmentPath: 'release',
  files: './tests/vscode/standalone-smoke.test.cjs',
  workspaceFolder: './tests/vscode/fixture',
  launchArgs: ['./tests/vscode/fixture/events/broken.txt']
};
