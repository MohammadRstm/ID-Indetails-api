import { Counter } from 'k6/metrics';
import { currentStage } from './profile.js';

/** Answers other than the expected ones, tagged by endpoint, status and the API's error code. */
export const unexpectedResponses = new Counter('unexpected_responses');

/** Counts an unexpected answer and writes its ERROR line: the API's error code and trace id, never the body. */
export function report(session, name, response) {
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
