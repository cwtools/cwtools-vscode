import typescript from 'rollup-plugin-typescript2';
import resolve from '@rollup/plugin-node-resolve';
import commonjs from '@rollup/plugin-commonjs';
import replace from '@rollup/plugin-replace';

export default {
    input: './client/webview/graph.ts',
    output: {
        format: "iife",
        name: "cwtoolsgraph",
        indent: false,
        // Add this banner to inject the process object at the beginning of your bundle
        banner: 'window.process = { env: { NODE_ENV: "development" } };'
    },
    plugins: [
        // Add replace plugin to handle any direct references to process.env
        replace({
            preventAssignment: true,
            'process.env.NODE_ENV': JSON.stringify('development'),
        }),
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