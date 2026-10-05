import { check } from 'k6';
import { SharedArray } from 'k6/data';
import { EMAIL_TEST, OUT, ROSTER, RUN_ID } from './lib/config.js';
import { field, get, post } from './lib/api.js';
import { newSession } from './lib/session.js';

// The email test: k6 run -e EMAIL_TEST=true email.js against an environment started with --email-test (the
// scheduler on). It calls every endpoint that sends email once, to addresses at @loadtest.invalid, and says how many
// emails Mailpit must have caught; the runner compares that with `lt.sh mail-count`. Nothing can reach a real
// mail server: the API has no route out of the server, and its SMTP server is Mailpit.

if (!EMAIL_TEST) {
  throw new Error('The email test runs only with EMAIL_TEST=true, against an environment started with lt.sh up --email-test.');
}

const roster = new SharedArray('roster', () => JSON.parse(open(ROSTER)).users.filter((u) => u.status === 'active'));
const first = (persona) => roster.find((u) => u.persona === persona);

export const options = {
  scenarios: { email: { executor: 'per-vu-iterations', vus: 1, iterations: 1, maxDuration: '5m' } },
  insecureSkipTLSVerify: true,
  thresholds: { 'checks{kind:email}': ['rate==1'] },
  tags: { run: RUN_ID, profile: 'email' },
};

export default function () {
  const design = newSession(first('DesignAdmin'));
  const execution = newSession(first('ExecutionAdmin'));
  let sent = 0;
  const expect = (name, response) => {
    const ok = response.status >= 200 && response.status < 300;
    check(response, { [`${name} succeeds`]: () => ok }, { kind: 'email' });
    if (ok) {
      sent++;
    }
  };

  // An invitation, then a fresh link for it: two emails.
  const invitee = `run.${RUN_ID}.invitee@loadtest.invalid`.toLowerCase();
  const invited = post(design, '/api/invitations', { email: invitee, role: 'employee', domainCode: 'design' }, { expect: [201] });
  expect('invitation', invited);
  const userId = field(invited, 'userId');
  if (userId) {
    expect('invitation resent', post(design, `/api/invitations/${userId}/resend`, null, { expect: [200, 204] }));
  }

  // A reset link for a test user (always 204; the email goes out in the background).
  expect('forgot password', post(design, '/api/auth/forgot-password', { email: first('DesignEmployee').email }, { expect: [204] }));

  // An issued invoice and a statement, emailed to their client's test address.
  const invoice = field(get(design, '/api/invoices?domain=design&status=sent&pageSize=1'), 'items.0', null);
  if (invoice) {
    const to = field(get(design, `/api/invoices/${invoice.id}`), 'billToContact', 'client@loadtest.invalid');
    expect('invoice emailed', post(design, `/api/invoices/${invoice.id}/email`, { to }, { expect: [200, 201, 204] }));
  }

  // The first client site that has a statement.
  let statement = null;
  for (const site of field(get(execution, '/api/projects?domain=execution&pageSize=20'), 'items', []).filter((p) => p.client)) {
    statement = field(get(execution, `/api/projects/${site.id}/statements?page=1&pageSize=1`), 'items.0', null);
    if (statement) {
      break;
    }
  }
  if (statement) {
    expect('statement emailed', post(execution, `/api/statements/${statement.id}/email`, { to: `run.${RUN_ID}.client@loadtest.invalid`.toLowerCase() }, { expect: [200, 204] }));
  }

  console.log(`EXPECTED_EMAILS ${sent}`);
}

export function handleSummary(data) {
  const checks = (data.root_group && data.root_group.checks) || [];
  return {
    stdout: `Email test, run ${RUN_ID}\n${checks.map((c) => `${c.fails === 0 ? 'ok    ' : 'FAILED'} ${c.name}`).join('\n')}\n`,
    [`${OUT}/k6-email-summary.json`]: JSON.stringify(data),
  };
}
