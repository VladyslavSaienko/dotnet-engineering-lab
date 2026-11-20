// LS-2.1-02 Sustained traffic: 100 products with stock 1,000 each; 200 virtual users for 2 minutes, random product.
// Purpose: throughput and latency under normal contention.
// Defaults not fixed by SPEC.md (quantity 1, no think time) are listed there under Open questions.

import { baseOptions, connect, purchase, randomInt, recordInventorySummary, reset } from './lib/inventory.js';
import { createSummaryHandler } from './lib/summary.js';

const PRODUCT_COUNT = 100;

export const options = {
    ...baseOptions,
    scenarios: {
        sustained: { executor: 'constant-vus', vus: 200, duration: '2m' },
    },
};

export function setup() {
    reset(PRODUCT_COUNT, 1000);
}

export default function () {
    connect();
    purchase(randomInt(1, PRODUCT_COUNT));
}

export function teardown() {
    recordInventorySummary();
}

export const handleSummary = createSummaryHandler('LS-2.1-02');
