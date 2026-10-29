import { del, field, get, post, put } from '../lib/api.js';
import { RUN_ID } from '../lib/config.js';
import { ledger, runMark } from '../lib/ledger.js';
import { DAY, day, dayStart, mondayOf, month, monthStart, pause, quarterStart, wallNow } from '../lib/time.js';
import { between, chance, domainOf, messages, meetings, notifications, pick, search, visitOne } from './common.js';
import { planMeeting } from './employees.js';

// Admins' and super admins' sessions: dashboards, project lists and pages with their changes, people, reports,
// clients and money. About one session in seven is a reporting session: a year of figures, exports and PDFs.

export async function admin(session) {
  const domain = session.user.domain;
  if (chance(0.15)) {
    return reporting(session, domain);
  }
  dashboard(session, domain);
  for (let views = between(3, 7); views > 0; views--) {
    await pause(5, 30);
    await visitOne(session, domain === 'design' ? DESIGN_ADMIN_PAGES : EXECUTION_ADMIN_PAGES);
  }
}

const SHARED_ADMIN_PAGES = [
  [14, (s) => dashboard(s, s.user.domain)],
  [16, (s) => projectList(s, s.user.domain)],
  [22, (s) => projectPage(s, s.user.domain)],
  [8, (s) => clients(s, s.user.domain)],
  [6, (s) => financials(s, s.user.domain)],
  [5, (s) => billing(s, s.user.domain)],
  [4, notifications],
  [4, messages],
  [2, meetings],
  [2, search],
];

const DESIGN_ADMIN_PAGES = [...SHARED_ADMIN_PAGES, [8, (s) => people(s)], [5, (s) => reports(s)], [4, (s) => activity(s)]];

const EXECUTION_ADMIN_PAGES = [...SHARED_ADMIN_PAGES, [8, (s) => siteStaff(s)]];

export async function superAdmin(session) {
  if (chance(0.15)) {
    return reporting(session, domainOf(session));
  }
  companyOverview(session);
  for (let views = between(3, 7); views > 0; views--) {
    await pause(5, 30);
    await visitOne(session, SUPER_ADMIN_PAGES);
  }
}

const SUPER_ADMIN_PAGES = [
  [16, (s) => companyOverview(s)],
  [14, (s) => salaries(s)],
  [10, (s) => overhead(s)],
  [8, (s) => projectPay(s)],
  [6, (s) => schedules(s)],
  [16, (s) => dashboard(s, domainOf(s))],
  [8, (s) => projectPage(s, domainOf(s))],
  [6, (s) => permissions(s)],
  [5, (s) => excuseHours(s)],
  [5, notifications],
  [4, messages],
  [2, search],
];

// ---- Domain pages ---------------------------------------------------------------------------------------------------

function dashboard(session, domain) {
  const now = wallNow();
  if (domain === 'design') {
    // Design's work report; Execution's dashboard has none.
    get(session, '/api/reports/overview?period=quarter');
  }
  get(session, `/api/dashboard/${domain}`);
  get(session, `/api/dashboard/${domain}/deadlines`);
  get(session, `/api/clients/overview?domain=${domain}`);
  if (domain === 'design') {
    get(session, '/api/work/people');
  }
  rememberProjects(session, domain, get(session, `/api/projects?domain=${domain}&pageSize=100`));
  get(session, `/api/finance/${domain}/summary?from=${day(quarterStart(now))}&to=${day(now)}`);
  get(session, `/api/finance/${domain}/monthly?months=6`);
  get(session, `/api/finance/${domain}/projects`);
  get(session, `/api/finance/${domain}/billing?months=6`);
  get(session, `/api/finance/${domain}/attention`);
  get(session, `/api/finance/${domain}/clients`);
}

function projectList(session, domain) {
  rememberProjects(session, domain, get(session, `/api/projects?domain=${domain}&sort=activity&page=1&pageSize=50`));
  get(session, `/api/projects/totals?domain=${domain}`);
  for (const status of ['', '&status=active', '&status=hold', '&status=completed', '&status=archived']) {
    get(session, `/api/projects?domain=${domain}${status}&page=1&pageSize=1`);
  }
  if (domain === 'design') {
    get(session, '/api/reports/overview?period=quarter');
  }
  get(session, `/api/clients?domain=${domain}&includeArchived=true&sort=name&pageSize=100`);
  get(session, `/api/employees?domain=${domain}&pageSize=100&status=active`);
}

function rememberProjects(session, domain, response) {
  const items = field(response, 'items', []);
  if (items.length > 0) {
    session.state[domain] = items.map((p) => ({ id: p.id, status: p.status }));
  }
}

function anyProject(session, domain, live = false) {
  if (!session.state[domain]) {
    rememberProjects(session, domain, get(session, `/api/projects?domain=${domain}&pageSize=100`));
  }
  const projects = (session.state[domain] || []).filter((p) => !live || p.status === 'active');
  return pick(projects);
}

/** A project's page, Design or Execution, then the changes admins make there. */
async function projectPage(session, domain) {
  const chosen = anyProject(session, domain);
  if (!chosen) {
    return;
  }
  const id = chosen.id;
  const now = wallNow();
  const project = field(get(session, `/api/projects/${id}`), null);
  get(session, `/api/projects/${id}/requirements`);
  get(session, `/api/projects/${id}/meetings`);
  if (domain === 'design') {
    get(session, `/api/projects/${id}/work`);
    if (project) {
      get(session, `/api/projects/${id}/work?from=${project.startDate}&to=${day(now)}`);
    }
    get(session, `/api/work-entries?projectId=${id}&page=1&pageSize=1`);
    get(session, `/api/projects/${id}/timeline`);
    get(session, `/api/projects/${id}/timeline/entries?from=${day(quarterStart(now))}&to=${day(now)}`);
    get(session, `/api/projects/${id}/expenses`);
    get(session, `/api/projects/${id}/cost-timeline`);
    get(session, `/api/invoices?projectId=${id}&pageSize=100`);
    get(session, '/api/employees?domain=design&pageSize=100');
  } else {
    get(session, `/api/projects/${id}/site-expenses?page=1&pageSize=100`);
    get(session, `/api/projects/${id}/labour-weeks`);
    get(session, `/api/projects/${id}/labour-defaults`);
    get(session, `/api/payments?projectId=${id}&pageSize=100`);
    get(session, `/api/invoices?projectId=${id}&pageSize=100`);
    get(session, `/api/projects/${id}/boqs`);
    get(session, `/api/projects/${id}/site-money/timeline`);
  }

  if (!project || project.status !== 'active') {
    return;
  }
  await pause(3, 15);
  if (chance(0.12)) {
    await addRequirement(session, id);
  }
  if (chance(0.08)) {
    editProject(session, project);
  }
  if (chance(0.08)) {
    await planMeeting(session, id);
  }
  if (domain === 'design' && chance(0.06)) {
    commentOnWork(session, id);
  }
  if (domain === 'design' && project.client && chance(0.04)) {
    await addExpense(session, id);
  }
}

async function addRequirement(session, projectId) {
  const deadline = day(dayStart(wallNow()) + 14 * DAY);
  const id = field(post(session, `/api/projects/${projectId}/requirements`, { text: `Check ${runMark}`, deadline, assigneeIds: [] }), 'id');
  if (!id) {
    return;
  }
  ledger('requirement.created', { id, project: projectId, user: session.user.userId });
  get(session, `/api/projects/${projectId}/requirements`);
  await pause(3, 10);
  if (chance(0.5)) {
    del(session, `/api/projects/${projectId}/requirements/${id}`);
    ledger('requirement.deleted', { id, project: projectId, user: session.user.userId });
  }
}

/** The project's details saved with a fresh note in the description, as an edit would. */
function editProject(session, project) {
  const description = `${(project.description || '').replace(/ \(reviewed [^)]*\)$/, '')} (reviewed ${day(wallNow())} ${runMark})`.slice(0, 1000);
  const saved = put(session, `/api/projects/${project.id}`, {
    title: project.title,
    description,
    location: project.location,
    icon: project.icon,
    tint: project.tint,
    targetDate: project.targetDate,
    ownerId: project.owner.id,
  });
  if (saved.status === 200) {
    ledger('project.updated', { id: project.id, user: session.user.userId, description });
  }
}

function commentOnWork(session, projectId) {
  const entry = pick(field(get(session, `/api/work-entries?projectId=${projectId}&page=1&pageSize=5`), 'items', []));
  if (!entry) {
    return;
  }
  get(session, `/api/work-entries/${entry.id}`);
  const id = field(post(session, `/api/work-entries/${entry.id}/comments`, { body: `Looks good ${runMark}`, sendToGroup: false }), 'id');
  if (id) {
    ledger('comment.created', { id, entry: entry.id, user: session.user.userId });
  }
}

async function addExpense(session, projectId) {
  const expense = { label: `LT printing ${runMark}`, amount: between(20, 400), category: 'services', spentOn: day(wallNow()) };
  const id = field(post(session, `/api/projects/${projectId}/expenses`, expense), 'id');
  if (!id) {
    return;
  }
  ledger('expense.created', { id, project: projectId, user: session.user.userId, amount: expense.amount });
  await pause(3, 10);
  if (chance(0.5)) {
    del(session, `/api/projects/${projectId}/expenses/${id}`);
    ledger('expense.deleted', { id, project: projectId, user: session.user.userId });
  }
}

/** Design: the people page and someone's page. */
function people(session) {
  const now = wallNow();
  const people = field(get(session, '/api/work/people'), null, []);
  get(session, '/api/projects?domain=design&pageSize=100');
  const person = pick(Array.isArray(people) ? people : []);
  if (!person) {
    return;
  }
  const userId = person.id;
  get(session, `/api/employees/${userId}`);
  get(session, `/api/employees/${userId}/work-summary`);
  get(session, `/api/work-entries?userId=${userId}&page=1&pageSize=18`);
  get(session, `/api/employees/${userId}/labour-cost`);
  // A super admin's overtime and pay are only for those who see the whole company: refusals here are the rules.
  get(session, `/api/employees/${userId}/overtime?month=${month(now)}`, { expect: [200, 403, 404] });
  get(session, `/api/employees/${userId}/pay`, { expect: [200, 403, 404] });
}

function siteStaff(session) {
  const staff = field(get(session, '/api/employees?domain=execution&status=active&page=1&pageSize=25'), 'items', []);
  const person = pick(staff);
  if (person) {
    get(session, `/api/employees/${person.id}`);
    get(session, `/api/employees/${person.id}/site-records`);
  }
}

function reports(session) {
  const now = wallNow();
  const from = day(quarterStart(now));
  get(session, `/api/reports/work?from=${from}&to=${day(now)}`);
  get(session, '/api/projects?domain=design&pageSize=100');
  get(session, '/api/work/people');
  get(session, '/api/finance/design/monthly?months=8');
  get(session, `/api/work-entries?from=${from}&to=${day(now)}&page=1&pageSize=50`);
  get(session, `/api/work-entries?from=${from}&to=${day(now)}&page=2&pageSize=50`);
}

function activity(session) {
  get(session, '/api/activity?domain=design&page=1&pageSize=40');
  get(session, '/api/activity/today');
  get(session, '/api/work/people');
}

/** The clients list and a client's page; now and then a new client, edited soon after. */
async function clients(session, domain) {
  const now = wallNow();
  const list = field(get(session, `/api/clients?domain=${domain}&page=1&pageSize=50&sort=revenue`), 'items', []);
  get(session, `/api/clients/overview?domain=${domain}`);
  get(session, `/api/finance/${domain}/summary?from=${day(monthStart(now))}&to=${day(now)}`);
  get(session, `/api/finance/${domain}/clients`);

  const client = pick(list);
  if (client) {
    get(session, `/api/clients/${client.id}`);
    get(session, `/api/projects?domain=${domain}&clientId=${client.id}&sort=start&pageSize=100`);
    get(session, `/api/invoices?clientId=${client.id}&pageSize=50`);
    get(session, `/api/payments?clientId=${client.id}&pageSize=4`);
  }

  if (chance(0.05)) {
    const n = `${session.user.userId}-${Date.now() % 100000}`;
    const created = {
      name: `LT Run ${RUN_ID} Client ${n}`,
      organisation: `LT Run ${RUN_ID} Client ${n} SAL`,
      label: 'New',
      note: runMark,
      domainCode: domain,
      contacts: [{ kind: 'email', value: `run.${RUN_ID}.client.${n}@loadtest.invalid`.toLowerCase(), isPrimary: true }],
    };
    const id = field(post(session, '/api/clients', created), 'id');
    if (id) {
      ledger('client.created', { id, domain, user: session.user.userId });
      await pause(3, 10);
      if (chance(0.5)) {
        put(session, `/api/clients/${id}`, { ...created, note: `${runMark} edited` });
        ledger('client.updated', { id, user: session.user.userId });
      }
    }
  }
}

function financials(session, domain) {
  const now = wallNow();
  const yearStart = `${day(now).slice(0, 4)}-01-01`;
  get(session, `/api/finance/${domain}/summary?from=${yearStart}&to=${day(now)}`);
  get(session, `/api/finance/${domain}/monthly?months=12`);
  get(session, `/api/finance/${domain}/projects`);
  get(session, `/api/finance/${domain}/clients`);
  get(session, `/api/invoices?domain=${domain}&pageSize=1`);
  get(session, `/api/projects?domain=${domain}&pageSize=100`);
  get(session, `/api/finance/${domain}/billing?months=12`);
  get(session, `/api/invoices?domain=${domain}&pageSize=100`);
  get(session, `/api/payments?domain=${domain}&pageSize=14`);
  get(session, `/api/finance/${domain}/costs?from=${yearStart}&to=${day(now)}`);
}

/**
 * The billing tab's changes: a draft written, corrected and discarded; sometimes one issued for real (numbering);
 * sometimes a small payment recorded on an open invoice, and sometimes voided again.
 */
async function billing(session, domain) {
  const invoices = field(get(session, `/api/invoices?domain=${domain}&pageSize=100`), 'items', []);
  get(session, `/api/payments?domain=${domain}&pageSize=14`);

  const billable = invoices.filter((i) => i.project && i.status !== 'void');
  const target = pick(billable);
  if (target) {
    const lines = [{ description: `LT draft ${runMark}`, amount: 100 }];
    const draftId = field(post(session, `/api/projects/${target.project.id}/invoices`, { lines, notes: runMark }, { expect: [201, 422] }), 'id');
    if (draftId) {
      ledger('invoice.drafted', { id: draftId, project: target.project.id, user: session.user.userId });
      await pause(3, 10);
      put(session, `/api/invoices/${draftId}`, { lines: [{ ...lines[0], amount: 150 }], notes: runMark });
      if (chance(0.1)) {
        const number = field(post(session, `/api/invoices/${draftId}/issue`), 'number');
        if (number) {
          ledger('invoice.issued', { id: draftId, number, project: target.project.id, user: session.user.userId, total: 150 });
        }
      } else {
        del(session, `/api/invoices/${draftId}`);
        ledger('invoice.discarded', { id: draftId, user: session.user.userId });
      }
    }
  }

  const open = pick(invoices.filter((i) => (i.status === 'sent' || i.status === 'partial') && Number(i.balance) >= 20));
  if (open && chance(0.3)) {
    get(session, `/api/invoices/${open.id}`);
    get(session, `/api/payments?invoiceId=${open.id}&pageSize=100`);
    const payment = {
      amount: 10, currency: 'USD', receivedOn: day(wallNow()), method: 'cash', reference: `LT-RUN-${RUN_ID}`, note: runMark, invoiceId: open.id,
    };
    const id = field(post(session, `/api/projects/${open.project.id}/payments`, payment, { expect: [201, 422] }), 'id');
    if (id) {
      ledger('payment.recorded', { id, invoice: open.id, project: open.project.id, user: session.user.userId, amountUsd: 10 });
      await pause(3, 10);
      if (chance(0.3)) {
        post(session, `/api/payments/${id}/void`, { reason: `LT ${runMark}` });
        ledger('payment.voided', { id, invoice: open.id, user: session.user.userId });
      }
    }
  }
}

/** A reporting session: a year of figures, the CSV exports and a few PDFs. */
async function reporting(session, domain) {
  financials(session, domain);
  await pause(5, 20);
  get(session, `/api/finance/${domain}/projects/export`);
  if (domain === 'design') {
    const now = wallNow();
    reports(session);
    get(session, `/api/work-entries/export?from=${day(quarterStart(now))}&to=${day(now)}`);
  }
  const issued = field(get(session, `/api/invoices?domain=${domain}&pageSize=100`), 'items', []).filter((i) => i.number);
  for (let i = 0; i < 2; i++) {
    const invoice = pick(issued);
    if (invoice) {
      await pause(3, 10);
      get(session, `/api/invoices/${invoice.id}/pdf`);
    }
  }
}

// ---- Company pages (super admins) ---------------------------------------------------------------------------------

function companyOverview(session) {
  const now = wallNow();
  get(session, `/api/company/overview?from=${day(monthStart(now))}&to=${day(now)}`);
  get(session, '/api/employees?status=active&page=1&pageSize=25');
  get(session, '/api/teams');
}

function salaries(session) {
  const lastMonth = month(monthStart(wallNow()) - DAY);
  const list = field(get(session, `/api/company/salaries?month=${lastMonth}&page=1&pageSize=10`), 'items', []);
  const person = pick(list);
  const userId = person && (person.userId || person.id);
  if (userId) {
    get(session, `/api/company/salaries/${userId}?month=${lastMonth}`);
    get(session, `/api/employees/${userId}/hours?page=1`);
  }
}

function overhead(session) {
  get(session, '/api/company/overhead');
  get(session, '/api/company/overview/monthly?months=13');
  get(session, `/api/company/overview/monthly?year=${day(wallNow()).slice(0, 4)}`);
  get(session, '/api/company/overhead/groups');
}

function projectPay(session) {
  get(session, `/api/company/project-pay?unit=week&date=${day(mondayOf(wallNow()))}`);
  get(session, '/api/projects?domain=design&pageSize=100');
  get(session, '/api/company/working-time');
}

function schedules(session) {
  get(session, '/api/company/schedules?domain=design');
  get(session, '/api/company/schedules?domain=execution');
}

function permissions(session) {
  get(session, '/api/permissions');
  const people = field(get(session, '/api/employees?status=active&page=1&pageSize=25'), 'items', []);
  const person = pick(people.filter((p) => p.role !== 'super_admin'));
  if (person) {
    get(session, `/api/users/${person.id}/permissions`);
  }
}

/** Half an hour excused on someone's ledger this month, sometimes taken back. */
async function excuseHours(session) {
  const people = field(get(session, '/api/employees?status=active&page=1&pageSize=25'), 'items', []);
  const person = pick(people.filter((p) => p.role === 'employee'));
  if (!person) {
    return;
  }
  const excuse = { month: month(wallNow()), kind: 'minutes', minutes: 30, isPaid: true, note: runMark };
  const id = field(post(session, `/api/employees/${person.id}/hour-excuses`, excuse), 'id');
  if (!id) {
    return;
  }
  ledger('hour_excuse.created', { id, person: person.id, user: session.user.userId });
  await pause(3, 10);
  if (chance(0.5)) {
    del(session, `/api/hour-excuses/${id}`);
    ledger('hour_excuse.deleted', { id, user: session.user.userId });
  }
}
