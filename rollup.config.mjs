import typescript from 'rollup-plugin-typescript2';
import resolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';

export default {
    input: './client/webview/graph.ts',
    output: {
        format: "iife",
        name: "cwtoolsgraph",
        indent: false,
    },
    plugins: [
        typescript({
            tsconfig: "tsconfig.webview.json",
            clean: true,
            tsconfigOverride: {
                exclude: ["client/test/**/*", "**/*.test.ts", "client/extension/**", "client/common/**"]
            }
        }),
        resolve({
            browser: true,
            moduleDirectories: ['node_modules'],
            extensions: ['.ts', '.js'],
            // Only include specific module types
            resolveOnly: [
                /^(?!.*test).*$/  // Exclude any paths containing 'test'
            ]
        }),
        commonjs({
            sourceMap: false,
            include: [
                'node_modules/**',
                'client/webview/**'
            ],
            exclude: [
                'client/test/**',
                'client/common/**',
                'client/extension/**',
                '**/*.test.ts'
            ]
        }),
    ]
}