import exec from 'k6/execution';

// The test users from the seed's roster, active ones only, interleaved by persona so that any number of virtual users
// from the first is the company's mix: the first 50 hold about as many admins and designers as the real 50 people.

const ORDER = ['DesignEmployee', 'ExecutionEmployee', 'DesignAdmin', 'ExecutionAdmin', 'SuperAdmin'];

export function interleave(rosterText) {
  const roster = JSON.parse(rosterText);
  const groups = ORDER.map((persona) => roster.users.filter((u) => u.persona === persona && u.status === 'active'));
  const total = groups.reduce((sum, group) => sum + group.length, 0);
  const taken = groups.map(() => 0);
  const users = [];
  while (users.length < total) {
    // Next from the persona furthest behind its share.
    let pick = -1;
    groups.forEach((group, i) => {
      if (taken[i] < group.length && (pick < 0 || taken[i] / group.length < taken[pick] / groups[pick].length)) {
        pick = i;
      }
    });
    users.push(groups[pick][taken[pick]++]);
  }
  return users;
}

/**
 * The person this virtual user plays. Virtual user n is always user n, so no two share a session; the profile never
 * runs more virtual users than the roster has active users (main.js checks it).
 */
export function userOf(users) {
  return users[(exec.vu.idInTest - 1) % users.length];
}
