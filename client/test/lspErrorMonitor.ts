import * as assert from 'assert';
import * as vscode from 'vscode';
import { defaultClient } from '../extension/extension';

interface ErrorEntry {
    timestamp: number;
    message: string;
}

// Global state for error monitoring
let errorLog: ErrorEntry[] = [];
let testStartTime: number = 0;
let originalAppendLine: ((value: string) => void) | undefined;
let isMonitoringActive = false;

/**
 * Sets up LSP error monitoring by intercepting the output channel
 * This should be called once at the start of each test
 */
export function setupLSPErrorMonitoring(): void {
    // Mark the start time for this test
    testStartTime = Date.now();

    // Only setup the interceptor once
    if (!isMonitoringActive && defaultClient && defaultClient.outputChannel) {
        originalAppendLine = defaultClient.outputChannel.appendLine.bind(defaultClient.outputChannel);

        defaultClient.outputChannel.appendLine = (message: string) => {
            const timestamp = Date.now();

            // Check for error messages and log them with timestamps
            if (message.toLowerCase().includes('error') ||
                message.toLowerCase().includes('exception') ||
                message.toLowerCase().includes('[Error')) {
                errorLog.push({
                    timestamp,
                    message: message
                });
            }

            // Call the original method
            return originalAppendLine!(message);
        };

        isMonitoringActive = true;
    } else {
        // If already monitoring, just update the test start time
        testStartTime = Date.now();
    }
}

/**
 * Checks for LSP errors that occurred since the test started
 * Fails the test if any errors are found
 */
export function checkForLSPErrors(testName: string): void {
    // Filter errors to only those that occurred during or after this test started
    const testErrors = errorLog.filter(entry => entry.timestamp >= testStartTime);
    if (testErrors.length > 0) {
        const errorMessages = testErrors.map(entry =>
            `[${new Date(entry.timestamp).toISOString()}] ${entry.message}`
        ).join('\n');

        // Remove the errors we're reporting so they don't affect future tests
        errorLog = errorLog.filter(entry => entry.timestamp < testStartTime);
        console.log(errorMessages)

        assert.fail(`LSP Server errors detected during test "${testName}":\n${errorMessages}`);
    }
}

/**
 * Completely tears down error monitoring (call this at the very end of all tests)
 */
export function teardownLSPErrorMonitoring(): void {
    if (isMonitoringActive && defaultClient && defaultClient.outputChannel && originalAppendLine) {
        // Restore the original appendLine method
        defaultClient.outputChannel.appendLine = originalAppendLine;
        originalAppendLine = undefined;
        isMonitoringActive = false;
    }

    // Clear the error log
    errorLog = [];
    testStartTime = 0;
}

/**
 * Gets the current error count for debugging purposes
 */
export function getErrorCount(): number {
    return errorLog.filter(entry => entry.timestamp >= testStartTime).length;
}

/**
 * Clears errors that occurred before the current test (for cleanup between test suites)
 */
export function clearPreviousErrors(): void {
    errorLog = errorLog.filter(entry => entry.timestamp >= testStartTime);
}