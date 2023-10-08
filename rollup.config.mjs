// rollup.config.js
import typescript from 'rollup-plugin-typescript';
import resolve from 'rollup-plugin-node-resolve';
import commonjs from 'rollup-plugin-commonjs';
import hypothetical from 'rollup-plugin-hypothetical';

export default {
    input: './client/webview/graph.ts',
    output: {
        format: "iife",
        name: "cwtoolsgraph",
        indent: false,
    },
    plugins: [
        typescript({
            "noUnusedLocals": false,
            "noUnusedParameters": true,
            "noImplicitAny": true,
            "noImplicitReturns": true,
            "target": "es6",
            "moduleResolution": "node",
            "rootDir": "client",
            "outDir": "out/client",
            "lib": ["es2016", "dom"],
            "sourceMap": true,
            "esModuleInterop": true,
            "outFile": "out/client/webview/graph.js",
            "lib": ["dom"],

        }),
        resolve(),
        commonjs({ sourceMap: false }),
        hypothetical({
            allowFallthrough: true,
            files: {
              'webworker-threads': `
                export default {};
              `,
              'web-worker': 'export default {};'
            }
          }),

    ]
}
