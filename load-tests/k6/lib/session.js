import http from 'k6/http';
import { BASE_URL, GATE_KEY, USER_PASSWORD } from './config.js';
import { currentStage } from './profile.js';

// One signed-in person per virtual user, as one browser. The access token lives in memory; the refresh cookie lives
// in the virtual user's own cookie jar, like the browser's, so no two virtual users ever share a session.

let current = null;

function params(name, session) {
  return {
    headers: { 'Content-Type': 'application/json', ...(GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {}) },
    tags: { name, persona: session.user.persona, stage: currentStage() },
    responseCallback: http.expectedStatuses(200),
    timeout: '60s',
  };
}

/** A separate signed-in session, as another tab or device would have (setup code and races use these). */
export function newSession(user) {
  const session = { user, token: null, state: {}, sessions: 0 };
  signIn(session);
  return session;
}

/** This virtual user's session as that person, signing in the first time. */
export function sessionFor(user) {
  if (!current || current.user.email !== user.email) {
    current = { user, token: null, state: {}, sessions: 0 };
    signIn(current);
  }
  return current;
}

export function signIn(session) {
  const response = http.post(
    `${BASE_URL}/api/auth/login`,
    JSON.stringify({ email: session.user.email, password: USER_PASSWORD }),
    params('POST /api/auth/login', session),
  );
  if (response.status !== 200) {
    throw new Error(`Sign-in as ${session.user.email} answered ${response.status}.`);
  }
  session.token = response.json('accessToken');
}

/** As a page load does: the refresh cookie for a new access token. Signs in again if the session has ended. */
export function refreshSession(session) {
  const response = http.post(`${BASE_URL}/api/auth/refresh`, null, params('POST /api/auth/refresh', session));
  if (response.status === 200) {
    session.token = response.json('accessToken');
    return;
  }
  signIn(session);
}
