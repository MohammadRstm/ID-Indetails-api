import { del, field, get, post, put } from '../lib/api.js';
import { ledger, runMark } from '../lib/ledger.js';
import { DAY, HOUR, MINUTE, day, dayStart, mondayOf, month, pause, quarterStart, utcIso, wallNow } from '../lib/time.js';
import { between, chance, messages, meetings, notifications, pick, search, visitOne } from './common.js';

// Employees' sessions, page by page, with the calls the React client makes on each.

// ---- Design employees: their home page, logging time, their projects and their week --------------------------------

export async function designEmployee(session) {
  designHome(session);
  for (let views = between(3, 7); views > 0; views--) {
    await pause(5, 25);
    await visitOne(session, DESIGN_PAGES);
  }
}

const DESIGN_PAGES = [
  [30, (s) => designHome(s)],
  [28, (s) => logWork(s)],
  [14, (s) => designProject(s)],
  [8, (s) => myWeek(s)],
  [6, notifications],
  [6, messages],
  [4, meetings],
  [2, (s) => myPay(s)],
  [2, search],
];

function designHome(session) {
  const now = wallNow();
  const projects = field(get(session, '/api/me/projects'), null, []);
  session.state.projects = projects.filter((p) => p.acceptsWork).map((p) => p.id);
  get(session, `/api/me/schedule?date=${day(now)}`);
  get(session, '/api/me/work-summary');
  get(session, '/api/domain-settings/design');
  get(session, `/api/me/day?date=${day(now)}`);
  get(session, `/api/me/overtime?month=${month(now)}`);
  get(session, '/api/me/hours?page=1');
}

/**
 * Logs a piece of work in a free stretch of today (07:00 up to now), or of yesterday evening while it is still this
 * month, as the timeline would. Then, as people do, sometimes corrects it, deletes it, or undoes the delete.
 */
async function logWork(session) {
  if (!session.state.projects) {
    designHome(session);
  }
  const projectId = pick(session.state.projects || []);
  const slot = freeSlot(session);
  if (!projectId || !slot) {
    return;
  }

  const date = day(slot.start);
  get(session, `/api/me/day?date=${date}`);
  get(session, `/api/me/schedule?date=${date}`);
  const times = { startedAt: utcIso(slot.start), endedAt: utcIso(slot.end) };
  post(session, '/api/me/overtime-preview', times);
  post(session, '/api/me/overtime-preview', times);

  const created = post(session, '/api/work-entries', { projectId, description: `${pick(WORK)} ${runMark}`, ...times });
  const id = field(created, 'id');
  if (!id) {
    return;
  }
  session.state.mine.push({ id, start: slot.start, end: slot.end });
  ledger('work_entry.created', { id, user: session.user.userId, project: projectId, minutes: (slot.end - slot.start) / MINUTE });
  afterWorkChange(session, date);

  await pause(5, 20);
  if (chance(0.3)) {
    put(session, `/api/work-entries/${id}`, { projectId, description: `${pick(WORK)} (corrected) ${runMark}`, ...times });
    ledger('work_entry.updated', { id, user: session.user.userId });
    afterWorkChange(session, date);
  }

  if (chance(0.2)) {
    del(session, `/api/work-entries/${id}`);
    session.state.mine = session.state.mine.filter((entry) => entry.id !== id);
    ledger('work_entry.deleted', { id, user: session.user.userId });
    afterWorkChange(session, date);
    if (chance(0.3)) {
      post(session, `/api/work-entries/${id}/restore`);
      session.state.mine.push({ id, start: slot.start, end: slot.end });
      ledger('work_entry.restored', { id, user: session.user.userId });
      afterWorkChange(session, date);
    }
  }
}

/** A saved entry makes the client reload everything about work, projects and the company that is on screen. */
function afterWorkChange(session, date) {
  const now = wallNow();
  get(session, '/api/me/projects');
  get(session, '/api/me/work-summary');
  get(session, `/api/me/day?date=${date}`);
  get(session, `/api/me/overtime?month=${month(now)}`);
  get(session, '/api/me/hours?page=1');
  get(session, '/api/projects?domain=design&status=active&page=1&pageSize=1');
  get(session, '/api/company/settings');
  get(session, '/api/company/branding');
}

function freeSlot(session) {
  const now = wallNow();
  const today = dayStart(now);
  const windows = [[today + 7 * HOUR, now - 3 * MINUTE]];
  if (month(today - DAY) === month(today)) {
    // The seed's days end by 19:30.
    windows.push([today - DAY + 20 * HOUR, today - DAY + 23 * HOUR + 50 * MINUTE]);
  }
  session.state.mine = session.state.mine || [];

  for (let attempt = 0; attempt < 12; attempt++) {
    const [from, to] = pick(windows);
    const length = between(3, 12) * 5 * MINUTE;
    const steps = Math.floor((to - from - length) / (5 * MINUTE));
    if (steps < 0) {
      continue;
    }
    const start = from + between(0, steps) * 5 * MINUTE;
    const end = start + length;
    if (session.state.mine.every((entry) => end <= entry.start || start >= entry.end)) {
      return { start, end };
    }
  }
  return null;
}

/** A project they're on: its page, and sometimes they tick a requirement of theirs or set up a meeting. */
async function designProject(session) {
  if (!session.state.projects) {
    designHome(session);
  }
  const projectId = pick(session.state.projects || []);
  if (!projectId) {
    return;
  }

  get(session, `/api/projects/${projectId}`);
  const requirements = field(get(session, `/api/projects/${projectId}/requirements`), 'items', []);
  get(session, `/api/projects/${projectId}/meetings`);
  get(session, `/api/me/projects/${projectId}/work`);
  get(session, '/api/me/projects');

  const mine = requirements.filter((r) => !r.isDone && (r.assignees || []).some((a) => a.id === session.user.userId));
  if (mine.length > 0 && chance(0.2)) {
    await tick(session, projectId, pick(mine).id);
  }
  if (chance(0.08)) {
    await planMeeting(session, projectId);
  }
}

export async function tick(session, projectId, requirementId) {
  if (post(session, `/api/projects/${projectId}/requirements/${requirementId}/done`).status >= 300) {
    return;
  }
  ledger('requirement.done', { id: requirementId, project: projectId, user: session.user.userId });
  get(session, `/api/projects/${projectId}/requirements`);
  await pause(3, 10);
  if (chance(0.3)) {
    post(session, `/api/projects/${projectId}/requirements/${requirementId}/undone`);
    ledger('requirement.undone', { id: requirementId, project: projectId, user: session.user.userId });
  }
}

/** Tomorrow at a round time; sometimes moved half an hour, sometimes called off. */
export async function planMeeting(session, projectId) {
  const start = dayStart(wallNow()) + DAY + between(18, 32) * 30 * MINUTE;
  const meeting = { startsAt: utcIso(start), endsAt: utcIso(start + 45 * MINUTE), topic: `Review ${runMark}` };
  const id = field(post(session, `/api/projects/${projectId}/meetings`, meeting), 'id');
  if (!id) {
    return;
  }
  ledger('meeting.created', { id, project: projectId, user: session.user.userId });
  await pause(3, 10);
  if (chance(0.4)) {
    put(session, `/api/meetings/${id}`, { ...meeting, startsAt: utcIso(start + 30 * MINUTE), endsAt: utcIso(start + 75 * MINUTE) });
    ledger('meeting.moved', { id, user: session.user.userId });
  }
  if (chance(0.3)) {
    del(session, `/api/meetings/${id}`);
    ledger('meeting.deleted', { id, user: session.user.userId });
  }
}

function myWeek(session) {
  const now = wallNow();
  const monday = mondayOf(now);
  get(session, '/api/me/work-summary');
  get(session, `/api/me/work-summary?from=${day(quarterStart(now))}&to=${day(now)}`);
  get(session, `/api/me/work-entries?from=${day(monday)}&to=${day(monday + 6 * DAY)}&page=1&pageSize=100`);
  get(session, '/api/me/projects');
}

function myPay(session) {
  const now = wallNow();
  get(session, `/api/me/pay?month=${month(dayStart(now) - 32 * DAY)}`);
  get(session, `/api/me/schedule?date=${day(now)}`);
}

// ---- Execution employees: their sites, materials, labour reports and requirements ----------------------------------

export async function executionEmployee(session) {
  siteHome(session);
  for (let views = between(3, 7); views > 0; views--) {
    await pause(5, 25);
    await visitOne(session, EXECUTION_PAGES);
  }
}

const EXECUTION_PAGES = [
  [25, (s) => siteHome(s)],
  [25, (s) => recordMaterials(s)],
  [16, (s) => openSite(s)],
  [8, (s) => reportLabour(s)],
  [8, (s) => myRequirements(s)],
  [6, notifications],
  [6, messages],
  [3, search],
  [3, (s) => get(s, '/api/me/site-records')],
];

function siteHome(session) {
  const active = field(get(session, '/api/projects?domain=execution&status=active&pageSize=100'), 'items', []);
  get(session, '/api/projects?domain=execution&status=hold&pageSize=100');
  session.state.sites = active.map((p) => p.id);
  if (active.length > 0) {
    get(session, `/api/projects/site-summary?${active.slice(0, 20).map((p) => `ids=${p.id}`).join('&')}`);
  }
}

/** A site's page; returns the project as the API describes it. */
function openSite(session, siteId) {
  if (!session.state.sites) {
    siteHome(session);
  }
  const id = siteId || pick(session.state.sites || []);
  if (!id) {
    return null;
  }
  const project = field(get(session, `/api/projects/${id}`), null);
  get(session, `/api/projects/${id}/requirements`);
  get(session, `/api/projects/${id}/meetings`);
  get(session, `/api/projects/${id}/materials?page=1&pageSize=30`);
  get(session, `/api/projects/${id}/labour-weeks`);
  get(session, `/api/projects/${id}/labour-defaults`);
  get(session, `/api/projects/${id}/labour-weeks/${day(mondayOf(wallNow()))}`);
  return project;
}

/** A delivery or two, as recorded on site; now and then they fix or remove one of their own. */
async function recordMaterials(session) {
  const project = openSite(session);
  if (!project) {
    return;
  }

  const items = [];
  for (let i = between(1, 3); i > 0; i--) {
    const [name, unit, unitCost] = pick(MATERIALS);
    items.push({ name, quantity: between(2, 60), unit, unitCost, supplier: 'LT Supplier 01', note: runMark });
  }
  const added = field(post(session, `/api/projects/${project.id}/materials`, { items }), null, []);
  for (const material of added) {
    ledger('material.created', { id: material.id, project: project.id, user: session.user.userId });
  }
  get(session, `/api/projects/${project.id}/materials?page=1&pageSize=30`);

  const material = pick(added);
  await pause(3, 10);
  if (material && chance(0.15)) {
    put(session, `/api/projects/${project.id}/materials/${material.id}`, { ...items[0], quantity: items[0].quantity + 1 });
    ledger('material.updated', { id: material.id, user: session.user.userId });
  } else if (material && chance(0.1)) {
    del(session, `/api/projects/${project.id}/materials/${material.id}`);
    ledger('material.deleted', { id: material.id, user: session.user.userId });
  }
}

/** This week so far, Monday to today (never Sunday), from the site's first day. Saving again replaces it. */
function reportLabour(session) {
  const project = openSite(session);
  if (!project) {
    return;
  }
  const now = wallNow();
  const monday = mondayOf(now);
  const defaults = field(get(session, `/api/projects/${project.id}/labour-defaults`), null, null);
  const dailyPay = (defaults && (defaults.current || defaults).dailyPay) || 30;
  const days = [];
  for (let d = monday; d <= dayStart(now) && d < monday + 6 * DAY; d += DAY) {
    if (day(d) >= project.startDate) {
      days.push({ date: day(d), workers: between(3, 10), dailyPay });
    }
  }
  if (days.length === 0) {
    return;
  }
  // As the page does: the week first, and the form only when the person may change it (a week someone else
  // reported is theirs). Two people saving a new week at once: the second is refused (403).
  const week = field(get(session, `/api/projects/${project.id}/labour-weeks/${day(monday)}`), null, null);
  if (!week || !week.canEdit) {
    return;
  }
  const saved = put(session, `/api/projects/${project.id}/labour-weeks/${day(monday)}`, { days, note: runMark }, { expect: [200, 403] });
  if (saved.status === 200) {
    ledger('labour_week.saved', { project: project.id, week: day(monday), user: session.user.userId });
  }
}

async function myRequirements(session) {
  const mine = field(get(session, '/api/me/requirements'), null, []);
  const requirement = pick(Array.isArray(mine) ? mine : []);
  if (requirement && chance(0.2)) {
    await tick(session, requirement.project.id, requirement.id);
  }
}

const WORK = [
  'Floor plan revisions', 'Facade detailing', 'Render iteration', 'Lighting layout', 'Client presentation',
  'Joinery details', 'BOQ review', 'Permit drawings', 'Site visit notes', 'Material schedule',
];

const MATERIALS = [
  ['Cement bags 50kg', 'bag', 7.5], ['Sand', 'm³', 22], ['Ceramic tiles 60x60', 'm²', 18], ['Paint 18L', 'bucket', 60],
  ['Gypsum board', 'sheet', 9], ['PVC pipe 4in', 'length', 12], ['Electrical cable 2.5mm', 'roll', 45],
];
