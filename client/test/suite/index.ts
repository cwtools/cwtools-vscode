import * as path from 'path';
import Mocha from 'mocha';
import { glob } from 'glob';

export async function run(): Promise<void> {
    // Create the mocha test
    const mocha = new Mocha({
        ui: 'tdd',
        color: true,
    });

    const testsRoot = path.resolve(__dirname, '..');

    try {
        const files = await glob('**/**.test.js', { cwd: testsRoot });

        // Add files to the test suite
        files.forEach((f) => mocha.addFile(path.resolve(testsRoot, f)));

        return new Promise((c, e) => {
            try {
                // Run the mocha test
                mocha.run(failures => {
                    if (failures > 0) {
                        e(new Error(`${failures} tests failed.`));
                    } else {
                        c();
                    }
                });
            } catch (err) {
                e(err);
            }
        });
    } catch (err) {
        throw err;
    }
}
