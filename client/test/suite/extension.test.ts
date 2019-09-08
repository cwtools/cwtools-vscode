//
// Note: This example test is leveraging the Mocha test framework.
// Please refer to their documentation on https://mochajs.org/ for help.
//

// The module 'assert' provides assertion methods from node
import * as assert from 'assert';

// You can import and use all API from the 'vscode' module
// as well as import your extension to test it
import * as vscode from 'vscode';
import { it, describe, before } from 'mocha';
//import * as myExtension from '../../extension/extension';

// Defines a Mocha test suite to group tests of similar kind together
suite("Extension Tests", () => {

    // Defines a Mocha unit test
    test("Something 1", () => {
        assert.equal(-1, [1, 2, 3].indexOf(5));
        assert.equal(-1, [1, 2, 3].indexOf(0));
    });
});

before(() => {

})

suite(`Debug Integration Test: `, function() {
    test('Extension should be present', () => {
		assert.ok(vscode.extensions.getExtension('tboby.cwtools-vscode'));
	});

	test('should activate', function () {
		this.timeout(1 * 60 * 1000);
		return vscode.extensions.getExtension('tboby.cwtools-vscode').activate().then((_) => {
			assert.ok(true);
		});
	});

	describe('should have errors', function () {
		this.timeout(1 * 60 * 1000);
		it('should have errors', async function (done) {
			await vscode.extensions.getExtension('tboby.cwtools-vscode').activate().then((x) => {
			setTimeout(() => {
				let count = 0;
					x.default.diagnostics.forEach(([], [], []) => count++);
					assert.ok(count);
					done();
			}, 45000);
		})});
	});
});
