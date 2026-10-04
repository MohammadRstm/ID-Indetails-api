// A load profile (profiles/*.json): steps of so many users, each reached by a ramp and then held. Every request is
// tagged with the step it ran in ("70 users", or "ramp to 70"), so results and errors can be read per step.

function seconds(duration) {
  const match = /^(\d+)(s|m|h)$/.exec(duration);
  if (!match) {
    throw new Error(`Durations look like 30s, 5m or 2h, not ${duration}.`);
  }
  return Number(match[1]) * { s: 1, m: 60, h: 3600 }[match[2]];
}

export function loadProfile(text) {
  const profile = JSON.parse(text);
  if (!Array.isArray(profile.steps) || profile.steps.length === 0) {
    throw new Error(`Profile ${profile.name} has no steps.`);
  }
  return profile;
}

export function maxUsers(profile) {
  return Math.max(...profile.steps.map((step) => step.users));
}

/** k6 ramping-vus stages: each step's ramp, then its hold, then the ramp down. */
export function k6Stages(profile) {
  const stages = [];
  for (const step of profile.steps) {
    stages.push({ duration: step.ramp, target: step.users });
    stages.push({ duration: step.hold, target: step.users });
  }
  stages.push({ duration: profile.rampDown || '1m', target: 0 });
  return stages;
}

/** Each period of the test with its label, end (seconds from the start) included. */
function timeline(profile) {
  const periods = [];
  let at = 0;
  for (const step of profile.steps) {
    at += seconds(step.ramp);
    periods.push({ label: `ramp to ${step.users}`, until: at });
    at += seconds(step.hold);
    periods.push({ label: `${step.users} users`, until: at });
  }
  periods.push({ label: 'ramp down', until: Infinity });
  return periods;
}

export function stageLabels(profile) {
  return timeline(profile).map((period) => period.label);
}

let periods = [];
let startedAt = 0;

export function startStageClock(profile, startMillis) {
  periods = timeline(profile);
  startedAt = startMillis;
}

export function currentStage() {
  if (!startedAt) {
    return 'setup';
  }
  const elapsed = (Date.now() - startedAt) / 1000;
  return periods.find((period) => elapsed < period.until).label;
}
