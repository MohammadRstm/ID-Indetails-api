import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE_URL, EMAIL_TEST, GATE_KEY } from './config.js';
import { EMAIL_SENDING, ENDPOINTS } from './endpoints.js';
import { currentStage } from './profile.js';
import { refreshSession } from './session.js';

// Every call to the API goes through here: the gate key, the access token (refreshed once on a 401, as the client
// does), the tags results are read by, and the check that the answer was the one expected.

/** Answers other than the expected ones, tagged by endpoint, status and the API's error code. */
export const unexpectedResponses = new Counter('unexpected_responses');

const unlisted = new Set();

/** "GET /api/projects/:id/labour-weeks/:date": the path with its ids and dates taken out, without the query. */
export function endpointName(method, path) {
  const route = path
    .split('?')[0]
    .split('/')
    .map((part) => (/^\d+$/.test(part) ? ':id' : /^\d{4}-\d{2}-\d{2}$/.test(part) ? ':date' : part))
    .join('/');
  return `${method} ${route}`;
}

/**
 * Calls the API as the session's person. `expect` lists the statuses that count as success (default 200, 201, 204);
 * anything else is counted and logged as an ERROR line, with the API's error code and trace id but no body.
 */
export function call(session, method, path, body, options = {}) {
  const name = endpointName(method, path);
  if (!ENDPOINTS.includes(name) && !unlisted.has(name)) {
    unlisted.add(name);
    console.log(`UNLISTED ${name} is measured but not broken down per step; add it to lib/endpoints.js`);
  }
  if (EMAIL_SENDING.includes(name) && !EMAIL_TEST) {
    throw new Error(`${name} sends email: only the email test may call it.`);
  }

  const expect = options.expect || [200, 201, 204];
  const send = () =>
    http.request(method, BASE_URL + path, body === undefined || body === null ? null : JSON.stringify(body), {
      headers: {
        Accept: 'application/json',
        ...(body !== undefined && body !== null ? { 'Content-Type': 'application/json' } : {}),
        ...(GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {}),
        ...(session.token ? { Authorization: `Bearer ${session.token}` } : {}),
      },
      tags: { name, persona: session.user.persona, stage: currentStage() },
      responseCallback: http.expectedStatuses(...expect),
      timeout: '60s',
    });

  let response = send();
  if (response.status === 401 && !options.noRefresh) {
    refreshSession(session);
    response = send();
  }

  const ok = expect.includes(response.status);
  check(response, { [`${name} answers as expected`]: () => ok });
  if (!ok) {
    report(session, name, response);
  }
  return response;
}

export const get = (session, path, options) => call(session, 'GET', path, null, options);
export const post = (session, path, body, options) => call(session, 'POST', path, body, options);
export const put = (session, path, body, options) => call(session, 'PUT', path, body, options);
export const del = (session, path, options) => call(session, 'DELETE', path, null, options);

/** A JSON field of a successful answer (the whole body when the path is empty), or the fallback. */
export function field(response, path, fallback = null) {
  if (response.status < 200 || response.status >= 300 || !response.body) {
    return fallback;
  }
  try {
    const value = path ? response.json(path) : response.json();
    return value === undefined ? fallback : value;
  } catch (_) {
    return fallback;
  }
}

function report(session, name, response) {
  let code = response.status === 0 ? `k6_${response.error_code}` : '';
  let traceId = null;
  if (response.status >= 400 && response.body) {
    try {
      const error = JSON.parse(response.body);
      code = /^[a-z_]{1,40}$/.test(error.code) ? error.code : 'unknown';
      traceId = error.traceId || null;
    } catch (_) {
      code = 'not_json';
    }
  }

  const stage = currentStage();
  unexpectedResponses.add(1, { name, status: String(response.status), code, stage });
  console.log(`ERROR ${JSON.stringify({ t: Date.now(), stage, name, status: response.status, code, traceId, persona: session.user.persona })}`);
}
