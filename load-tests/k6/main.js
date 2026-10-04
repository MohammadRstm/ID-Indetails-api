import { SharedArray } from 'k6/data';
import exec from 'k6/execution';
import http from 'k6/http';
import { BASE_URL, GATE_KEY, LIVE, OUT, PROFILE, ROSTER, RUN_ID } from './lib/config.js';
import { ENDPOINTS } from './lib/endpoints.js';
import { k6Stages, loadProfile, maxUsers, stageLabels, startStageClock } from './lib/profile.js';
import { interleave, userOf } from './lib/users.js';
import { refreshSession, sessionFor } from './lib/session.js';
import { openLive } from './lib/live.js';
import { pause, setProfileThink } from './lib/time.js';
import { onLiveSignal, shell } from './journeys/common.js';
import { designEmployee, executionEmployee } from './journeys/employees.js';
import { admin, superAdmin } from './journeys/admins.js';

// The load test: k6 run -e PROFILE=stages -e BASE_URL=... main.js (load-tests/README.md).
//
// Every virtual user is one test user from the roster, always the same one, and plays sessions in the app as that
// person: the app shell and the live connection, then a few pages with a person's pauses, and the changes people make
// there. Between sessions they are away for a while. The profile decides how many are in the app over time.

const profile = loadProfile(open(`./profiles/${PROFILE}.json`));
const users = new SharedArray('users', () => interleave(open(ROSTER)));

if (maxUsers(profile) > users.length) {
  throw new Error(`Profile ${profile.name} needs ${maxUsers(profile)} users; the roster has ${users.length} active. Seed more (--users).`);
}

const JOURNEYS = {
  DesignEmployee: designEmployee,
  ExecutionEmployee: executionEmployee,
  DesignAdmin: admin,
  ExecutionAdmin: admin,
  SuperAdmin: superAdmin,
};

export const options = {
  scenarios: {
    users: { executor: 'ramping-vus', startVUs: 0, stages: k6Stages(profile), gracefulRampDown: '30s', gracefulStop: '30s' },
  },
  // The load-test proxy's certificate comes from its own local authority.
  insecureSkipTLSVerify: true,
  thresholds: thresholds(),
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
  tags: { run: RUN_ID, profile: profile.name },
  userAgent: 'hourline-load-test',
};

/**
 * The profile's pass/fail thresholds, plus always-passing ones on each step and each endpoint (and each endpoint in
 * each steady step): k6 keeps the numbers of what a threshold names, and the report is made from them.
 */
function thresholds() {
  const all = { ...profile.thresholds };
  const steps = stageLabels(profile);
  for (const step of steps) {
    all[`http_req_duration{stage:${step}}`] = ['max>=0'];
    all[`http_req_failed{stage:${step}}`] = ['rate>=0'];
    all[`http_reqs{stage:${step}}`] = ['count>=0'];
    all[`unexpected_responses{stage:${step}}`] = ['count>=0'];
  }
  for (const name of ENDPOINTS) {
    all[`http_req_duration{name:${name}}`] = ['max>=0'];
    all[`http_req_failed{name:${name}}`] = ['rate>=0'];
    for (const step of steps.filter((s) => s.endsWith(' users'))) {
      all[`http_req_duration{name:${name},stage:${step}}`] = ['max>=0'];
    }
  }
  return all;
}

/** Stops before any load when the environment doesn't answer as the load-test one. */
export function setup() {
  const response = http.get(`${BASE_URL}/api/company/branding`, {
    headers: GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {},
    tags: { name: 'setup' },
  });
  if (response.status !== 200) {
    throw new Error(`${BASE_URL} answered ${response.status}; is the load-test environment up, and the gate key right?`);
  }
  console.log(`Profile ${profile.name}: up to ${maxUsers(profile)} of ${users.length} test users; live connections ${LIVE ? 'on' : 'off'}.`);
}

export default async function () {
  startStageClock(profile, exec.scenario.startTime);
  setProfileThink(profile.think || 1);

  const user = userOf(users);
  const session = sessionFor(user);
  if (session.sessions++ > 0) {
    // A later session starts as a page load does: a new access token from the refresh cookie.
    refreshSession(session);
  }

  shell(session);
  const live = LIVE ? openLive(session, (signal) => onLiveSignal(session, signal)) : null;
  try {
    await JOURNEYS[user.persona](session);
  } finally {
    if (live) {
      live.close();
    }
  }
  await pause(10, 60);
}

export function handleSummary(data) {
  const lines = [`Profile ${profile.name}, run ${RUN_ID}`];
  for (const [name, metric] of Object.entries(data.metrics)) {
    for (const [threshold, result] of Object.entries(metric.thresholds || {})) {
      if (!result.ok) {
        lines.push(`FAILED threshold ${name}: ${threshold}`);
      }
    }
  }
  const duration = data.metrics.http_req_duration && data.metrics.http_req_duration.values;
  const failed = data.metrics.http_req_failed && data.metrics.http_req_failed.values;
  const requests = data.metrics.http_reqs && data.metrics.http_reqs.values;
  if (duration && requests) {
    lines.push(
      `${requests.count} requests (${requests.rate.toFixed(1)}/s), failed ${((failed ? failed.rate : 0) * 100).toFixed(2)}%, ` +
        `p50 ${duration.med.toFixed(0)} ms, p95 ${duration['p(95)'].toFixed(0)} ms, p99 ${duration['p(99)'].toFixed(0)} ms`,
    );
  }
  // The profile and the end time go with the numbers, so the report can tell each step's length and clock times.
  return {
    stdout: `${lines.join('\n')}\n`,
    [`${OUT}/k6-summary.json`]: JSON.stringify({ ...data, loadProfile: profile, finishedAt: Date.now(), run: RUN_ID }),
  };
}
