// Unrecorded warm-up before measured runs: JIT, connection pools and SQL Server caches are warm,
// so run 1 is not systematically slower than runs 2 and 3. run.ps1 runs it once per invocation.

import { baseOptions, connect, purchase, randomInt, reset } from './lib/inventory.js';

const PRODUCT_COUNT = 100;

export const options = {
    ...baseOptions,
    thresholds: {},
    scenarios: {
        warmup: { executor: 'constant-vus', vus: 50, duration: '15s' },
    },
};

export function setup() {
    reset(PRODUCT_COUNT, 1000);
}

export default function () {
    connect();
    purchase(randomInt(1, PRODUCT_COUNT));
}
