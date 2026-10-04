import { check } from 'k6';
import { SharedArray } from 'k6/data';
import http from 'k6/http';
import { BASE_URL, GATE_KEY, OUT, ROSTER, RUN_ID, USER_PASSWORD } from './lib/config.js';

// The rate-limit test: k6 run rate-limits.js against an environment started with production's per-IP limits
// (lt.sh up --production-rate-limits). The normal tests raise those limits because all their traffic comes from one
// address; this checks the real ones still answer 429 too_many_requests, and never anything worse. Each check runs in
// its own minute, so one window's count doesn't spill into the next.

const user = new SharedArray('user', () => [JSON.parse(open(ROSTER)).users.find((u) => u.status === 'active' && u.persona === 'DesignEmployee')])[0];
const headers = { 'Content-Type': 'application/json', ...(GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {}) };

export const options = {
  scenarios: {
    sign_in: { executor: 'per-vu-iterations', exec: 'signIns', vus: 1, iterations: 1, startTime: '0s' },
    anonymous: { executor: 'per-vu-iterations', exec: 'anonymous', vus: 1, iterations: 1, startTime: '70s' },
    per_user: { executor: 'per-vu-iterations', exec: 'perUser', vus: 1, iterations: 1, startTime: '140s' },
    refresh: { executor: 'per-vu-iterations', exec: 'refreshes', vus: 1, iterations: 1, startTime: '210s' },
  },
  insecureSkipTLSVerify: true,
  thresholds: { 'checks{kind:limit}': ['rate==1'] },
  tags: { run: RUN_ID, profile: 'rate-limits' },
};

function signIn() {
  return http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({ email: user.email, password: USER_PASSWORD }), { headers, tags: { name: 'POST /api/auth/login' } });
}

/** Statuses of `count` requests in a row, and whether the limit held: some 429 with its code, no 5xx, success before it. */
function judge(name, statuses, responses) {
  const firstLimited = statuses.indexOf(429);
  const limited = responses.find((r) => r.status === 429);
  console.log(`LIMIT ${JSON.stringify({ name, sent: statuses.length, firstLimitedAt: firstLimited + 1, statuses: countBy(statuses) })}`);
  check(null, {
    [`${name}: over the limit answers 429`]: () => firstLimited > 0,
    [`${name}: the 429 says too_many_requests`]: () => !!limited && limited.json('code') === 'too_many_requests',
    [`${name}: never a server error`]: () => statuses.every((s) => s > 0 && s < 500),
    [`${name}: requests within the limit succeed`]: () => statuses.slice(0, Math.max(firstLimited, 0)).every((s) => s < 400),
  }, { kind: 'limit' });
}

function countBy(statuses) {
  return statuses.reduce((counts, s) => ({ ...counts, [s]: (counts[s] || 0) + 1 }), {});
}

/** Sign-in is limited per address (10 a minute in production). */
export function signIns() {
  const responses = Array.from({ length: 15 }, () => signIn());
  judge('sign-in per address', responses.map((r) => r.status), responses);
}

/** Requests without a valid token are limited per address (60 a minute). */
export function anonymous() {
  const responses = Array.from({ length: 75 }, () => http.get(`${BASE_URL}/api/company/branding`, { headers, tags: { name: 'GET /api/company/branding' } }));
  judge('anonymous per address', responses.map((r) => r.status), responses);
}

/** A signed-in person is limited per person (300 a minute). */
export function perUser() {
  const token = signIn().json('accessToken');
  const authorized = { headers: { ...headers, Authorization: `Bearer ${token}` }, tags: { name: 'GET /api/auth/me' } };
  const responses = Array.from({ length: 320 }, () => http.get(`${BASE_URL}/api/auth/me`, authorized));
  judge('signed-in per person', responses.map((r) => r.status), responses);
}

/** Refreshing is limited per address: each refresh rotates the cookie, so they are sent one after another. */
export function refreshes() {
  signIn();
  const responses = [];
  for (let i = 0; i < 70; i++) {
    const response = http.post(`${BASE_URL}/api/auth/refresh`, null, { headers, tags: { name: 'POST /api/auth/refresh' } });
    responses.push(response);
    if (response.status === 429) {
      break;
    }
  }
  judge('refresh per address', responses.map((r) => r.status), responses);
}

export function handleSummary(data) {
  const checks = (data.root_group && data.root_group.checks) || [];
  const lines = checks.map((c) => `${c.fails === 0 ? 'ok    ' : 'FAILED'} ${c.name}`);
  return { stdout: `Rate limits, run ${RUN_ID}\n${lines.join('\n')}\n`, [`${OUT}/k6-rate-limits-summary.json`]: JSON.stringify(data) };
}
