// rollup.config.js
import typescript from 'rollup-plugin-typescript';
import resolve from 'rollup-plugin-node-resolve';
import commonjs from 'rollup-plugin-commonjs';
import inject from 'rollup-plugin-inject';

export default {
    input: './client/webview/graph.ts',
    output: {
        format: "iife",
        name: "cwtoolsgraph"
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
        commonjs(),
        inject({
            // control which files this plugin applies to
            // with include/exclude
            include: '**/*.js',
            exclude: 'node_modules/**',

            /* all other options are treated as modules...*/

            // use the default – i.e. insert
            // import $ from 'jquery'
            $: 'jquery'
        })
    ]
}
