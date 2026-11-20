// Shared building blocks for the LS-2.1-* scenarios: configuration, the API calls and the custom metrics.
// Every scenario uses these so that only the load shape differs between scripts.

import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Gauge, Rate, Trend } from 'k6/metrics';

export const BASE_URL = (__ENV.BASE_URL || 'http://localhost:53643').replace(/\/+$/, '');
export const VARIANT = __ENV.VARIANT || 'V0';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

// 409 is a valid business outcome, not a failure: keeps http_req_failed equal to the error rate.
http.setResponseCallback(http.expectedStatuses(201, 409));

export const metrics = {
    succeeded: new Counter('purchases_succeeded'),
    rejected: new Counter('purchases_rejected'),
    errors: new Counter('purchases_errors'),
    errorRate: new Rate('purchase_error_rate'),
    duration: new Trend('purchase_duration', true),
    // min(start) and max(end) give the window in which purchases were in flight. Throughput is computed
    // over that window, so the time spent in setup (reset) and teardown (summary) does not dilute it.
    windowStart: new Gauge('purchase_window_start_ms'),
    windowEnd: new Gauge('purchase_window_end_ms'),
    // Filled in teardown() from GET /test-support/summary; teardown samples are part of the end-of-test data.
    initialStock: new Gauge('inventory_initial_stock'),
    sold: new Gauge('inventory_sold'),
    oversold: new Gauge('inventory_oversold'),
};

// The only thresholds: we measure latency, we do not gate on it. A breach makes k6 exit with code 99.
export const thresholds = {
    purchase_error_rate: ['rate<0.01'],
};

export const baseOptions = {
    discardResponseBodies: true,
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(95)', 'p(99)'],
    thresholds,
};

export function reset(productCount, initialStock) {
    const res = http.post(
        `${BASE_URL}/test-support/reset`,
        JSON.stringify({ productCount, initialStock }),
        { headers: JSON_HEADERS, tags: { name: 'reset' } },
    );
    if (res.status !== 204) {
        fail(`Reset failed with status ${res.status}. Is the variant running in the Development environment at ${BASE_URL}?`);
    }
}

// Opening 1,000 TCP connections to one socket at the same instant overflows the listen backlog: on Windows 10
// about 70 % of them were refused by the OS before the variant saw them, with and without the Aspire proxy.
// So each VU opens its keep-alive connection once, staggered, before its first purchase; purchases reuse it.
// Optionally the VU then waits until startAtMs, so that a burst still starts at the same moment for every VU.
const CONNECT_SPACING_SECONDS = 0.002;

export function connectionWindowMs(vus) {
    return Math.ceil(vus * CONNECT_SPACING_SECONDS * 1000) + 1000;
}

export function connect(startAtMs) {
    if (exec.vu.iterationInScenario > 0) {
        return;
    }
    sleep((exec.vu.idInTest - 1) * CONNECT_SPACING_SECONDS);
    // Any response opens the connection; the request is not part of the purchase metrics.
    http.get(`${BASE_URL}/alive`, {
        tags: { name: 'connect' },
        responseCallback: http.expectedStatuses({ min: 200, max: 499 }),
    });
    if (startAtMs) {
        sleep(Math.max(0, startAtMs - Date.now()) / 1000);
    }
}

export function purchase(productId, quantity = 1) {
    metrics.windowStart.add(Date.now());
    const res = http.post(
        `${BASE_URL}/purchases`,
        JSON.stringify({ productId, quantity }),
        { headers: JSON_HEADERS, tags: { name: 'purchase' } },
    );
    metrics.windowEnd.add(Date.now());

    const succeeded = res.status === 201;
    const rejected = res.status === 409;
    check(res, { 'status is 201 or 409': () => succeeded || rejected });

    // Only answered purchases have a meaningful latency; errors (e.g. refused connections) are counted separately.
    if (succeeded || rejected) {
        metrics.duration.add(res.timings.duration);
    }
    metrics.errorRate.add(!succeeded && !rejected);
    if (succeeded) {
        metrics.succeeded.add(1);
    } else if (rejected) {
        metrics.rejected.add(1);
    } else {
        metrics.errors.add(1);
    }
}

export function recordInventorySummary() {
    const res = http.get(`${BASE_URL}/test-support/summary`, { responseType: 'text', tags: { name: 'summary' } });
    if (res.status !== 200) {
        fail(`Summary failed with status ${res.status}.`);
    }

    const products = res.json('products');
    const sum = (field) => products.reduce((total, p) => total + p[field], 0);
    metrics.initialStock.add(sum('initialStock'));
    metrics.sold.add(sum('sold'));
    metrics.oversold.add(sum('oversold'));
}

export function randomInt(min, max) {
    return min + Math.floor(Math.random() * (max - min + 1));
}
