// The endpoints the journeys call, by the names api.js gives them. Results are broken down per endpoint and per load
// step for these (main.js declares a threshold for each pair, which is what makes k6 keep those numbers). A call
// missing from this list is still measured, only not broken down: main.js warns about it at the end.

const DOMAIN_ENDPOINTS = [
  'GET /api/dashboard/{domain}', 'GET /api/dashboard/{domain}/deadlines',
  'GET /api/finance/{domain}/attention', 'GET /api/finance/{domain}/billing', 'GET /api/finance/{domain}/clients',
  'GET /api/finance/{domain}/costs', 'GET /api/finance/{domain}/monthly', 'GET /api/finance/{domain}/projects',
  'GET /api/finance/{domain}/projects/export', 'GET /api/finance/{domain}/summary',
];

export const ENDPOINTS = [
  ...['design', 'execution'].flatMap((domain) => DOMAIN_ENDPOINTS.map((name) => name.replace('{domain}', domain))),
  'POST /api/auth/login', 'POST /api/auth/refresh', 'GET /api/auth/me', 'POST /hubs/live/negotiate',
  'GET /api/company/branding', 'GET /api/company/settings', 'GET /api/company/overview', 'GET /api/company/overview/monthly',
  'GET /api/company/overhead', 'GET /api/company/overhead/groups', 'GET /api/company/project-pay', 'GET /api/company/salaries',
  'GET /api/company/salaries/:id', 'GET /api/company/schedules', 'GET /api/company/working-time',
  'GET /api/domain-settings/design', 'GET /api/teams', 'GET /api/permissions', 'GET /api/users/:id/permissions',
  'GET /api/employees', 'GET /api/employees/:id', 'GET /api/employees/:id/hours', 'GET /api/employees/:id/labour-cost',
  'GET /api/employees/:id/overtime', 'GET /api/employees/:id/pay', 'GET /api/employees/:id/site-records',
  'GET /api/employees/:id/work-summary', 'POST /api/employees/:id/hour-excuses', 'DELETE /api/hour-excuses/:id',
  'GET /api/clients', 'GET /api/clients/:id', 'GET /api/clients/overview', 'POST /api/clients', 'PUT /api/clients/:id',
  'GET /api/projects', 'GET /api/projects/totals', 'GET /api/projects/site-summary', 'GET /api/projects/:id', 'PUT /api/projects/:id',
  'GET /api/projects/:id/requirements', 'POST /api/projects/:id/requirements', 'DELETE /api/projects/:id/requirements/:id',
  'POST /api/projects/:id/requirements/:id/done', 'POST /api/projects/:id/requirements/:id/undone',
  'GET /api/projects/:id/meetings', 'POST /api/projects/:id/meetings', 'GET /api/meetings', 'PUT /api/meetings/:id',
  'DELETE /api/meetings/:id', 'GET /api/projects/:id/expenses', 'POST /api/projects/:id/expenses',
  'DELETE /api/projects/:id/expenses/:id', 'GET /api/projects/:id/work', 'GET /api/projects/:id/timeline',
  'GET /api/projects/:id/timeline/entries', 'GET /api/projects/:id/cost-timeline', 'GET /api/projects/:id/materials',
  'POST /api/projects/:id/materials', 'PUT /api/projects/:id/materials/:id', 'DELETE /api/projects/:id/materials/:id',
  'GET /api/projects/:id/labour-weeks', 'GET /api/projects/:id/labour-weeks/:date', 'PUT /api/projects/:id/labour-weeks/:date',
  'GET /api/projects/:id/labour-defaults', 'GET /api/projects/:id/statements', 'POST /api/projects/:id/statements/preview',
  'GET /api/projects/:id/site-money/timeline', 'GET /api/invoices', 'GET /api/invoices/:id', 'GET /api/invoices/:id/pdf',
  'POST /api/projects/:id/invoices', 'PUT /api/invoices/:id', 'DELETE /api/invoices/:id', 'POST /api/invoices/:id/issue',
  'GET /api/payments', 'POST /api/projects/:id/payments', 'POST /api/payments/:id/void',
  'GET /api/me/day', 'GET /api/me/hours', 'GET /api/me/overtime', 'GET /api/me/pay', 'GET /api/me/projects',
  'GET /api/me/projects/:id/work', 'GET /api/me/requirements', 'GET /api/me/schedule', 'GET /api/me/work-entries',
  'GET /api/me/work-summary', 'GET /api/me/site-records', 'POST /api/me/overtime-preview',
  'GET /api/work-entries', 'GET /api/work-entries/:id', 'GET /api/work-entries/export', 'POST /api/work-entries',
  'PUT /api/work-entries/:id', 'DELETE /api/work-entries/:id', 'POST /api/work-entries/:id/restore',
  'POST /api/work-entries/:id/comments', 'GET /api/work/people', 'GET /api/reports/overview', 'GET /api/reports/work',
  'GET /api/activity', 'GET /api/activity/today', 'GET /api/search', 'GET /api/presence',
  'GET /api/notifications', 'GET /api/notifications/unread-count', 'POST /api/notifications/read-all',
  'GET /api/messages/badge', 'GET /api/messages/contacts', 'GET /api/messages/conversations',
  'GET /api/messages/teams/:id', 'GET /api/messages/teams/:id/members', 'POST /api/messages/teams/:id',
  'POST /api/messages/teams/:id/read', 'GET /api/messages/direct/:id', 'POST /api/messages/direct/:id',
  'POST /api/messages/direct/:id/read',
];

/**
 * Every endpoint that sends email (every caller of the API's IEmailSender), plus the two that schedule invoices the
 * scheduler later emails. api.js refuses them unless EMAIL_TEST is set: normal tests test the application, not the
 * mail provider.
 */
export const EMAIL_SENDING = [
  'POST /api/auth/forgot-password',
  'POST /api/auth/change-email',
  'PUT /api/employees/:id/email',
  'POST /api/invitations',
  'POST /api/invitations/:id/resend',
  'POST /api/invoices/:id/email',
  'POST /api/statements/:id/email',
  'POST /api/setup/super-admin',
  'POST /api/projects/:id/invoice-plan',
  'PUT /api/invoices/:id/schedule',
];
