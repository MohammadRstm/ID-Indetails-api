// What the scripts read from the environment (k6 run -e NAME=value). The gate key and the test users' password only
// ever live in memory: nothing here is written to a file or a log.

function required(name) {
  const value = __ENV[name];
  if (!value) {
    throw new Error(`Set ${name} (k6 run -e ${name}=...). See load-tests/README.md.`);
  }
  return value;
}

function offsetMinutes(text) {
  const match = /^([+-])(\d{2}):?(\d{2})$/.exec(text);
  if (!match) {
    throw new Error(`COMPANY_UTC_OFFSET must look like +03:00, not ${text}.`);
  }
  return (match[1] === '-' ? -1 : 1) * (Number(match[2]) * 60 + Number(match[3]));
}

/** The load-test environment, e.g. https://203.0.113.10:8443. Never production: its users don't exist there. */
export const BASE_URL = required('BASE_URL').replace(/\/$/, '');

export const WS_URL = BASE_URL.replace(/^http/, 'ws');

/** Sent as X-LoadTest-Key on every request; the load-test proxy answers 404 without it. */
export const GATE_KEY = __ENV.GATE_KEY || '';

/** Every test user's password (LT_USER_PASSWORD when the data was seeded). */
export const USER_PASSWORD = required('USER_PASSWORD');

/** The roster the seed wrote, relative to load-tests/k6. */
export const ROSTER = __ENV.ROSTER || '../reports/roster.json';

export const PROFILE = __ENV.PROFILE || 'smoke';

/** Marks what this run creates, so verification can tell it from the seeded data. */
export const RUN_ID = __ENV.RUN_ID || 'local';

/** Where handleSummary writes the summary, relative to the working directory. */
export const OUT = __ENV.OUT || 'load-tests/reports';

/** Only the dedicated email test may call the endpoints that send email. */
export const EMAIL_TEST = __ENV.EMAIL_TEST === 'true';

/** Hold a live (SignalR) connection during each session, as the browser does. */
export const LIVE = __ENV.LIVE !== 'false';

/** The company's UTC offset (Beirut: +03:00 in summer, +02:00 in winter), as the runner works it out. */
export const COMPANY_OFFSET_MINUTES = offsetMinutes(__ENV.COMPANY_UTC_OFFSET || '+03:00');

/** Scales every pause: 1 is a person's pace, 0.2 five times faster. The profile multiplies it again. */
export const THINK = Number(__ENV.THINK || '1');
