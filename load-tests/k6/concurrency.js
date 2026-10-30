import { check } from 'k6';
import { SharedArray } from 'k6/data';
import exec from 'k6/execution';
import http from 'k6/http';
import { BASE_URL, GATE_KEY, OUT, ROSTER, RUN_ID } from './lib/config.js';
import { call, field, get, post, put } from './lib/api.js';
import { ledger, runMark } from './lib/ledger.js';
import { newSession, sessionFor } from './lib/session.js';
import { DAY, MINUTE, day, dayStart, mondayOf, wallNow } from './lib/time.js';

// The race tests: k6 run -e BASE_URL=... concurrency.js (load-tests/README.md).
//
// Each race is a scenario of several people, each signed in in their own session, who do something to the same
// record at the same instant: they sign in first, wait for the shared moment, then fire. Every answer must be one the
// rules allow (never a 500), and afterwards the record must be in a state some order of those requests could have
// left it in. The tool's verify then checks the database as a whole.

const roster = new SharedArray('roster', () => JSON.parse(open(ROSTER)).users.filter((u) => u.status === 'active' && !u.partTime));
const of = (persona) => roster.filter((u) => u.persona === persona);
const SPACING = 45;
const FIRE_AFTER = 20;

/**
 * Each race: who takes part (one user per position), what each does at the moment, which answers are allowed, and
 * what must hold afterwards. `prepare` runs in setup and returns what the race works on; `recorded` writes the
 * ledger lines of an accepted change, as the journeys do, so verify can match the database with them.
 */
const RACES = [
  {
    name: 'same_project_edit',
    who: () => [...of('DesignAdmin'), ...of('SuperAdmin')],
    prepare: (admin) => ({ project: field(get(admin, '/api/projects?domain=design&status=active&pageSize=1'), 'items.0.id') }),
    act: (s, f) => {
      const project = field(get(s, `/api/projects/${f.project}`), null);
      return put(s, `/api/projects/${f.project}`, {
        title: project.title, description: `Edited by ${s.user.userId} ${runMark}`, location: project.location,
        icon: project.icon, tint: project.tint, targetDate: project.targetDate, ownerId: project.owner.id,
      }, { expect: [200] });
    },
    afterwards: (admin, f, people) => {
      const description = field(get(admin, `/api/projects/${f.project}`), 'description', '');
      return { 'the project holds one of the edits, whole': people.some((u) => description === `Edited by ${u.userId} ${runMark}`) };
    },
  },
  {
    name: 'requirement_ticks',
    who: () => of('DesignEmployee').slice(0, 10),
    prepare: (admin, people) => {
      const project = field(get(admin, '/api/projects?domain=design&status=active&pageSize=1&page=2'), 'items.0.id');
      const requirements = people.map((u) => field(post(admin, `/api/projects/${project}/requirements`,
        { text: `Race tick ${runMark}`, deadline: day(dayStart(wallNow()) + 7 * DAY), assigneeIds: [u.userId] }), 'id'));
      requirements.forEach((id) => ledger('requirement.created', { id, project, user: admin.user.userId }));
      return { project, requirements };
    },
    act: (s, f, i) => post(s, `/api/projects/${f.project}/requirements/${f.requirements[i]}/done`, null, { expect: [200] }),
    recorded: (s, f, i) => ledger('requirement.done', { id: f.requirements[i], project: f.project, user: s.user.userId }),
    afterwards: (admin, f) => {
      const list = field(get(admin, `/api/projects/${f.project}/requirements`), null, {});
      const progress = field(get(admin, `/api/projects/${f.project}`), 'progressPct');
      return {
        'every raced requirement is done': f.requirements.every((id) => (list.items || []).some((r) => r.id === id && r.isDone)),
        "the project's progress is its done share": progress === Math.round((list.done * 100) / list.total),
      };
    },
  },
  {
    name: 'same_requirement_ticks',
    who: () => of('DesignEmployee').slice(10, 13),
    prepare: (admin, people) => {
      const project = field(get(admin, '/api/projects?domain=design&status=active&pageSize=1&page=3'), 'items.0.id');
      const requirement = field(post(admin, `/api/projects/${project}/requirements`,
        { text: `Race shared tick ${runMark}`, assigneeIds: people.map((u) => u.userId) }), 'id');
      ledger('requirement.created', { id: requirement, project, user: admin.user.userId });
      return { project, requirement };
    },
    act: (s, f) => post(s, `/api/projects/${f.project}/requirements/${f.requirement}/done`, null, { expect: [200, 403, 422] }),
    recorded: (s, f) => ledger('requirement.done', { id: f.requirement, project: f.project, user: s.user.userId }),
    afterwards: (admin, f) => {
      const list = field(get(admin, `/api/projects/${f.project}/requirements`), null, {});
      const progress = field(get(admin, `/api/projects/${f.project}`), 'progressPct');
      return { "the project's progress is its done share": progress === Math.round((list.done * 100) / list.total) };
    },
  },
  {
    name: 'payments_same_invoice',
    who: () => of('DesignAdmin').filter((u) => !u.joker).slice(0, 6),
    prepare: (admin) => {
      const project = clientProject(admin, 'design');
      const id = field(post(admin, `/api/projects/${project}/invoices`, { lines: [{ description: `Race ${runMark}`, amount: 100 }] }), 'id');
      post(admin, `/api/invoices/${id}/issue`);
      return { project, invoice: id };
    },
    act: (s, f) => post(s, `/api/projects/${f.project}/payments`, {
      amount: 30, currency: 'USD', receivedOn: day(wallNow()), method: 'cash', reference: `LT-RACE-${RUN_ID}`, invoiceId: f.invoice,
    }, { expect: [201, 422] }),
    afterwards: (admin, f, people) => {
      const payments = field(get(admin, `/api/payments?invoiceId=${f.invoice}&pageSize=100`), 'items', []);
      const paid = payments.reduce((sum, p) => sum + Number(p.amountUsd), 0);
      return {
        'the invoice is never overpaid': paid <= 100,
        'as many payments as fit were taken': payments.length === Math.min(people.length, 3),
      };
    },
  },
  {
    name: 'invoice_numbering',
    who: () => of('DesignAdmin').filter((u) => !u.joker),
    prepare: (admin, people) => {
      const project = clientProject(admin, 'design');
      return { drafts: people.map(() => field(post(admin, `/api/projects/${project}/invoices`, { lines: [{ description: `Race ${runMark}`, amount: 50 }] }), 'id')) };
    },
    act: (s, f, i) => post(s, `/api/invoices/${f.drafts[i]}/issue`, null, { expect: [200] }),
    afterwards: (admin, f) => {
      const numbers = f.drafts.map((id) => field(get(admin, `/api/invoices/${id}`), 'number'));
      const sequence = numbers.map((n) => Number(String(n).split('-').pop())).sort((a, b) => a - b);
      return {
        'every raced draft got a number': numbers.every((n) => n),
        'the numbers are distinct and consecutive': sequence.every((n, i) => i === 0 || n === sequence[i - 1] + 1),
      };
    },
  },
  {
    name: 'boqs_same_project',
    who: () => of('ExecutionAdmin').slice(0, 3),
    prepare: (admin) => ({ project: field(get(admin, '/api/projects?domain=execution&status=active&pageSize=1'), 'items.0.id') }),
    act: (s, f) => post(s, `/api/projects/${f.project}/boqs`, { kind: 'client', title: `Race BOQ ${runMark}` }, { expect: [201] }),
    afterwards: (admin, f) => {
      const numbers = field(get(admin, `/api/projects/${f.project}/boqs`), 'client', [])
        .filter((b) => String(b.title).includes(runMark)).map((b) => Number(String(b.number).split('-').pop())).sort((a, b) => a - b);
      return {
        'every raced BOQ got a number': numbers.length === 3,
        'the numbers are distinct and consecutive': numbers.every((n, i) => i === 0 || n === numbers[i - 1] + 1),
      };
    },
  },
  {
    name: 'same_user_two_tabs',
    who: () => [of('DesignEmployee')[13], of('DesignEmployee')[13]],
    prepare: (admin, people) => {
      const project = field(get(newSession(people[0]), '/api/me/projects'), null, []).filter((p) => p.acceptsWork)[0];
      return { project: project && project.id, ...pastSlot(30) };
    },
    act: (s, f) => post(s, '/api/work-entries', { projectId: f.project, description: `Race tab ${runMark}`, startedAt: f.startedAt, endedAt: f.endedAt }, { expect: [201, 422] }),
    recorded: (s, f, i, response) => workEntryLogged(s, f, response),
    afterwards: () => ({}),
  },
  {
    name: 'labour_week_same_week',
    who: () => of('ExecutionEmployee').slice(0, 3),
    prepare: (admin) => {
      const project = field(get(admin, '/api/projects?domain=execution&status=active&pageSize=1&page=3'), null, {});
      return { project: project.items[0].id, startDate: project.items[0].startDate, week: day(mondayOf(wallNow())) };
    },
    act: (s, f, i) => {
      const today = dayStart(wallNow());
      const days = [];
      for (let d = mondayOf(today); d <= today && d < mondayOf(today) + 6 * DAY; d += DAY) {
        days.push({ date: day(d), workers: 3 + i, dailyPay: 30 });
      }
      // The first report makes the week its reporter's; the others may only change their own weeks (403).
      return put(s, `/api/projects/${f.project}/labour-weeks/${f.week}`, { days, note: `Race ${i} ${runMark}` }, { expect: [200, 403, 409, 422] });
    },
    recorded: (s, f) => ledger('labour_week.saved', { project: f.project, week: f.week, user: s.user.userId }),
    afterwards: (admin, f) => ({ 'the week holds one report': field(get(admin, `/api/projects/${f.project}/labour-weeks/${f.week}`), 'weekStart', null) !== null }),
  },
  {
    name: 'permissions_same_person',
    who: () => of('SuperAdmin').slice(0, 2),
    prepare: () => ({ person: of('ExecutionEmployee')[3].userId }),
    act: (s, f, i) => put(s, `/api/users/${f.person}/permissions`, {
      changes: [{ code: i === 0 ? 'client:view' : 'report:view', action: 'grant', reason: runMark }],
    }, { expect: [200] }),
    afterwards: (admin, f) => {
      const access = field(get(admin, `/api/users/${f.person}/permissions`), null, {});
      const held = JSON.stringify(access);
      return { 'both grants are kept': held.includes('client:view') && held.includes('report:view') };
    },
  },
  {
    name: 'exchange_rate_same_day',
    who: () => of('SuperAdmin').slice(0, 2),
    // A rate may start today at the latest, after the currency's latest one: only one of the two can take today.
    prepare: () => ({ on: day(wallNow()) }),
    act: (s, f, i) => post(s, '/api/exchange-rates', { currency: 'LBP', ratePerUsd: 89500 + 100 * i, effectiveFrom: f.on }, { expect: [201, 422] }),
    afterwards: (admin, f) => {
      const rates = field(get(admin, '/api/exchange-rates'), null, []);
      return { 'the currency has one rate from that day': rates.filter((r) => r.currency === 'LBP' && r.effectiveFrom === f.on).length === 1 };
    },
  },
  {
    name: 'complete_while_logging',
    who: () => [of('DesignAdmin').filter((u) => !u.joker)[0], ...of('DesignEmployee').slice(14, 19)],
    prepare: (admin, people) => {
      const project = field(get(admin, '/api/projects?domain=design&status=active&pageSize=1&page=4'), 'items.0.id');
      return { project, ...pastSlot(20) };
    },
    act: (s, f, i) => (i === 0
      ? post(s, `/api/projects/${f.project}/complete`, null, { expect: [200] })
      : post(s, '/api/work-entries', { projectId: f.project, description: `Race log ${runMark}`, startedAt: f.startedAt, endedAt: f.endedAt }, { expect: [201, 422] })),
    recorded: (s, f, i, response) => i > 0 && workEntryLogged(s, f, response),
    afterwards: () => ({}),
  },
  {
    // One person, two tabs refreshing with the same cookie at once: one wins, the other gets 401, and the person
    // stays signed in (it is a re-sent cookie within seconds, not a stolen one).
    name: 'refresh_same_cookie',
    who: () => [of('DesignEmployee')[19]],
    prepare: () => ({}),
    act: (s) => {
      const params = { headers: GATE_KEY ? { 'X-LoadTest-Key': GATE_KEY } : {}, tags: { name: 'POST /api/auth/refresh' } };
      const responses = http.batch([
        ['POST', `${BASE_URL}/api/auth/refresh`, null, params],
        ['POST', `${BASE_URL}/api/auth/refresh`, null, params],
      ]);
      const statuses = responses.map((r) => r.status).sort();
      const winner = responses.find((r) => r.status === 200);
      const stillIn = winner && http.get(`${BASE_URL}/api/auth/me`, { headers: { ...params.headers, Authorization: `Bearer ${winner.json('accessToken')}` } }).status === 200;
      check(null, {
        'refresh_same_cookie: one refresh wins and the other gets 401': () => statuses.join() === '200,401',
        'refresh_same_cookie: the person is still signed in': () => !!stillIn,
      }, { kind: 'race' });
      return winner || responses[0];
    },
    afterwards: () => ({}),
  },
].filter((race) => race.who().length >= 1 && race.who().every((u) => u));

/** A slot that ended a quarter of an hour ago, in whole minutes, so it is in the past whenever the race fires. */
function pastSlot(minutes) {
  const end = Math.floor((Date.now() - 15 * MINUTE) / MINUTE) * MINUTE;
  return { startedAt: new Date(end - minutes * MINUTE).toISOString(), endedAt: new Date(end).toISOString(), minutes };
}

function workEntryLogged(session, f, response) {
  ledger('work_entry.created', { id: response.json('id'), user: session.user.userId, project: f.project, minutes: f.minutes });
}

function clientProject(admin, domain) {
  const projects = field(get(admin, `/api/projects?domain=${domain}&status=active&billingType=client&pageSize=20`), 'items', []);
  return projects.length > 0 ? projects[projects.length - 1].id : null;
}

export const options = {
  scenarios: Object.fromEntries(RACES.map((race, i) => [race.name, {
    executor: 'per-vu-iterations', exec: 'race', vus: race.who().length, iterations: 1,
    startTime: `${i * SPACING}s`, maxDuration: `${SPACING - 5}s`, env: { RACE: race.name },
  }])),
  insecureSkipTLSVerify: true,
  setupTimeout: '10m',
  teardownTimeout: '5m',
  thresholds: { 'checks{kind:race}': ['rate==1'], 'checks{kind:afterwards}': ['rate==1'] },
  tags: { run: RUN_ID, profile: 'concurrency' },
};

/** Signs in an admin of each kind and prepares every race; returns when each one fires. */
export function setup() {
  const started = Date.now();
  const admins = { design: newSession(of('DesignAdmin')[0]), execution: newSession(of('ExecutionAdmin')[0]), super: newSession(of('SuperAdmin')[0]) };
  const fixtures = {};
  for (const race of RACES) {
    const admin = race.name.includes('boq') || race.name.includes('labour') ? admins.execution
      : race.name.includes('permission') || race.name.includes('exchange') ? admins.super : admins.design;
    fixtures[race.name] = race.prepare(admin, race.who());
  }
  // Scenarios start when setup returns; each fires FIRE_AFTER seconds into its own slot.
  const fireAt = Object.fromEntries(RACES.map((race, i) => [race.name, Date.now() + (i * SPACING + FIRE_AFTER) * 1000]));
  console.log(`Prepared ${RACES.length} races in ${Math.round((Date.now() - started) / 1000)}s: ${RACES.map((r) => r.name).join(', ')}`);
  return { fixtures, fireAt };
}

export async function race(data) {
  const current = RACES.find((r) => r.name === __ENV.RACE);
  const index = exec.scenario.iterationInTest;
  const person = current.who()[index];
  const session = sessionFor(person);
  await new Promise((resolve) => setTimeout(resolve, Math.max(0, data.fireAt[current.name] - Date.now())));

  const response = current.act(session, data.fixtures[current.name], index);
  if (current.recorded && response.status >= 200 && response.status < 300) {
    current.recorded(session, data.fixtures[current.name], index, response);
  }
  let code = '';
  if (response.status >= 400) {
    try {
      code = response.json('code') || '';
    } catch (_) {
      code = 'not_json';
    }
  }
  ledger('race', { race: current.name, user: person.userId, position: index, status: response.status, code });
  check(response, { [`${current.name}: an allowed answer, never a server error`]: (r) => r.status > 0 && r.status < 500 }, { kind: 'race' });
}

/** What must hold after each race, read back through the API. */
export function teardown(data) {
  const admin = newSession(of('SuperAdmin')[0]);
  for (const current of RACES) {
    for (const [rule, holds] of Object.entries(current.afterwards(admin, data.fixtures[current.name], current.who()))) {
      check(null, { [`${current.name}: ${rule}`]: () => holds }, { kind: 'afterwards' });
      console.log(`AFTERWARDS ${JSON.stringify({ race: current.name, rule, holds })}`);
    }
  }
}

export function handleSummary(data) {
  const checks = (data.root_group && data.root_group.checks) || [];
  const lines = checks.map((c) => `${c.fails === 0 ? 'ok    ' : 'FAILED'} ${c.name} (${c.passes}/${c.passes + c.fails})`);
  return { stdout: `Races, run ${RUN_ID}\n${lines.join('\n')}\n`, [`${OUT}/k6-concurrency-summary.json`]: JSON.stringify(data) };
}
