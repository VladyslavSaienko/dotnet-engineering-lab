// Turns the k6 end-of-test data into the ADR 0003 result document.
//
// Why k6 writes the result itself: teardown() reads GET /test-support/summary and records sold/oversold
// as gauges. Data returned from teardown() is not passed to handleSummary(), but metric samples emitted
// in teardown() are, so the result is complete without a separate merge step.
// run.ps1 provides what k6 cannot know (run number, timestamp, machine details) through environment variables.

import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';
import { VARIANT } from './inventory.js';

const EXPERIMENT = '2.1';

// open() is only available in the init context, so the environment file is read when the module loads.
const environment = __ENV.ENVIRONMENT_FILE ? JSON.parse(open(__ENV.ENVIRONMENT_FILE)) : null;

export function createSummaryHandler(scenario) {
    return (data) => {
        const text = textSummary(data, { indent: ' ', enableColors: false });
        const outputs = { stdout: text };

        if (__ENV.RESULT_FILE) {
            outputs[__ENV.RESULT_FILE] = JSON.stringify(buildResult(scenario, data), null, 2) + '\n';
        }
        if (__ENV.RAW_PREFIX) {
            outputs[`${__ENV.RAW_PREFIX}.summary.json`] = JSON.stringify(data, null, 2);
            outputs[`${__ENV.RAW_PREFIX}.summary.txt`] = text;
        }
        return outputs;
    };
}

function buildResult(scenario, data) {
    const value = (metric, stat) => data.metrics[metric]?.values?.[stat];
    // Counters without samples are absent from the summary.
    const count = (metric) => value(metric, 'count') ?? 0;

    const succeeded = count('purchases_succeeded');
    const rejected = count('purchases_rejected');
    const errors = count('purchases_errors');
    const windowMs = value('purchase_window_end_ms', 'max') - value('purchase_window_start_ms', 'min');

    return {
        experiment: EXPERIMENT,
        variant: VARIANT,
        scenario,
        run: Number(__ENV.RUN || 1),
        timestampUtc: __ENV.RUN_TIMESTAMP_UTC || new Date().toISOString().replace(/\.\d{3}Z$/, 'Z'),
        environment,
        metrics: {
            requestsPerSecond: round((succeeded + rejected + errors) / (windowMs / 1000), 1),
            latencyMs: {
                p50: round(value('purchase_duration', 'p(50)'), 2),
                p95: round(value('purchase_duration', 'p(95)'), 2),
                p99: round(value('purchase_duration', 'p(99)'), 2),
            },
            succeeded,
            rejected,
            errors,
            oversold: value('inventory_oversold', 'value') ?? null,
            // The API does not expose these yet; V1 (retries) and V2 (deadlocks) need a source first (SPEC open question 9).
            retries: null,
            deadlocks: null,
        },
    };
}

function round(number, digits) {
    const factor = 10 ** digits;
    return Math.round(number * factor) / factor;
}
