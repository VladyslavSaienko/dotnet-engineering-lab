// LS-2.1-01 Hot item: 1 product with stock 10; 1,000 virtual users each try to buy 1 copy, all starting together.
// Purpose: correctness under extreme contention (AC-2.1-01, AC-2.1-02).

import { baseOptions, connect, connectionWindowMs, purchase, recordInventorySummary, reset } from './lib/inventory.js';
import { createSummaryHandler } from './lib/summary.js';

const PRODUCT_ID = 1;
const VUS = 1000;

export const options = {
    ...baseOptions,
    scenarios: {
        hot_item: { executor: 'per-vu-iterations', vus: VUS, iterations: 1, maxDuration: '1m' },
    },
};

export function setup() {
    reset(1, 10);
    // Every VU opens its connection first, then all purchases are sent at this moment.
    return { startAtMs: Date.now() + connectionWindowMs(VUS) };
}

export default function (data) {
    connect(data.startAtMs);
    purchase(PRODUCT_ID);
}

export function teardown() {
    recordInventorySummary();
}

export const handleSummary = createSummaryHandler('LS-2.1-01');
