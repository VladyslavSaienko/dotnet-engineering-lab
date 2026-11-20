// LS-2.1-03 Spike: the LS-2.1-02 load plus a sudden burst on 1 product.
// Purpose: behaviour when contention appears suddenly.
// The burst shape is not fixed by SPEC.md; the defaults below are listed there under Open questions:
// - background traffic buys products 2-100, so product 1 still has its full stock when the burst starts;
// - 60 seconds in, 1,000 virtual users each buy 1 copy of product 1 twice in a row: 2,000 purchases, twice its stock.
//   The first 1,000 are sent at the same moment.

import exec from 'k6/execution';
import { baseOptions, connect, purchase, randomInt, recordInventorySummary, reset } from './lib/inventory.js';
import { createSummaryHandler } from './lib/summary.js';

const PRODUCT_COUNT = 100;
const HOT_PRODUCT_ID = 1;
const BURST_AT_SECONDS = 60;

export const options = {
    ...baseOptions,
    scenarios: {
        background: { executor: 'constant-vus', vus: 200, duration: '2m' },
        // Starts early so that every burst VU has its connection open at BURST_AT_SECONDS (see connect()).
        burst: { executor: 'per-vu-iterations', vus: 1000, iterations: 2, startTime: `${BURST_AT_SECONDS - 5}s`, maxDuration: '35s' },
    },
};

export function setup() {
    reset(PRODUCT_COUNT, 1000);
    return { burstAtMs: Date.now() + BURST_AT_SECONDS * 1000 };
}

export default function (data) {
    if (exec.scenario.name === 'burst') {
        connect(data.burstAtMs);
        purchase(HOT_PRODUCT_ID);
    } else {
        connect();
        purchase(randomInt(HOT_PRODUCT_ID + 1, PRODUCT_COUNT));
    }
}

export function teardown() {
    recordInventorySummary();
}

export const handleSummary = createSummaryHandler('LS-2.1-03');
