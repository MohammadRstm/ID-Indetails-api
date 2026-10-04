import { field, get, post } from '../lib/api.js';
import { ledger, runMark } from '../lib/ledger.js';
import { day, monthStart, wallNow } from '../lib/time.js';

// What every signed-in page does, and the pages every persona visits: notifications, messages, search, meetings.

export function pick(items) {
  return items.length === 0 ? null : items[Math.floor(Math.random() * items.length)];
}

export function chance(probability) {
  return Math.random() < probability;
}

export function between(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}

/** Calls one of the pages, chosen by weight: [[weight, page], ...]. */
export async function visitOne(session, pages) {
  const total = pages.reduce((sum, [weight]) => sum + weight, 0);
  let roll = Math.random() * total;
  for (const [weight, page] of pages) {
    roll -= weight;
    if (roll < 0) {
      return page(session);
    }
  }
  return pages[pages.length - 1][1](session);
}

export function domainOf(session) {
  return session.user.domain || (chance(0.6) ? 'design' : 'execution');
}

/** The app shell around every page: settings, branding, the sidebar counts the person may see, the top bar. */
export function shell(session) {
  const user = session.user;
  get(session, '/api/auth/me');
  get(session, '/api/company/settings');
  get(session, '/api/company/branding');
  if (user.domain) {
    get(session, `/api/projects?domain=${user.domain}&status=active&page=1&pageSize=1`);
    if (user.role === 'admin') {
      get(session, `/api/clients?domain=${user.domain}&page=1&pageSize=1`);
      get(session, `/api/employees?domain=${user.domain}&status=active&page=1&pageSize=1`);
    }
  } else {
    const now = wallNow();
    get(session, `/api/company/overview?from=${day(monthStart(now))}&to=${day(now)}`);
  }
  topBar(session);
}

export function topBar(session) {
  get(session, '/api/notifications/unread-count');
  get(session, '/api/messages/badge');
}

/** What the client reloads when the live connection signals a change. */
export function onLiveSignal(session, signal) {
  if (signal === 'notifications') {
    get(session, '/api/notifications/unread-count');
  } else {
    get(session, '/api/messages/badge');
  }
}

export function notifications(session) {
  get(session, '/api/notifications?page=1&pageSize=30');
  if (chance(0.3)) {
    get(session, '/api/notifications?page=1&pageSize=30&unreadOnly=true');
  }
  if (chance(0.4)) {
    post(session, '/api/notifications/read-all');
    get(session, '/api/notifications/unread-count');
  }
}

/** The messages page opens on the person's team chat (super admins: a direct message), and sometimes they write. */
export function messages(session) {
  get(session, '/api/messages/conversations');
  const contacts = field(get(session, '/api/messages/contacts'), null, []);
  get(session, '/api/presence');

  const teamId = session.user.teamId;
  const contactId = !teamId && Array.isArray(contacts) && contacts.length > 0 ? pick(contacts).id : null;
  const thread = teamId ? `/api/messages/teams/${teamId}` : contactId ? `/api/messages/direct/${contactId}` : null;
  if (!thread) {
    return;
  }

  get(session, `${thread}?limit=50`);
  if (teamId) {
    get(session, `/api/messages/teams/${teamId}/members`);
  }
  post(session, `${thread}/read`);

  if (chance(0.25)) {
    const sent = post(session, thread, { body: `${pick(CHAT)} ${runMark}` }, { expect: [200, 201] });
    const id = field(sent, 'id');
    if (id) {
      ledger('message.sent', { id, user: session.user.userId, team: teamId || null });
    }
    get(session, '/api/messages/badge');
    get(session, '/api/messages/conversations');
    get(session, `${thread}?limit=50`);
  }
}

export function search(session) {
  get(session, '/api/search');
  get(session, `/api/search?q=${encodeURIComponent(pick(SEARCHES))}`);
}

export function meetings(session) {
  const domain = domainOf(session);
  get(session, `/api/meetings?domain=${domain}`);
  get(session, `/api/meetings?domain=${domain}&mine=true`);
}

const CHAT = [
  'Updated the drawings, please have a look.', 'On my way to the site.', 'Can we move the review to 3?',
  'Client confirmed the materials.', 'Thanks!', 'I will take this one.',
];

const SEARCHES = ['LT-D0', 'LT-X0', 'Villa', 'Beirut', 'LT Design Client', 'Employee 01', 'renovation'];
