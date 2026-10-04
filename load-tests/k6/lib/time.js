import { COMPANY_OFFSET_MINUTES, THINK } from './config.js';

// Company dates as the API sees them (Asia/Beirut). A "wall" time is milliseconds whose UTC fields read as the
// company's local date and time.
export const MINUTE = 60 * 1000;
export const HOUR = 60 * MINUTE;
export const DAY = 24 * HOUR;

let profileThink = 1;

export function setProfileThink(factor) {
  profileThink = factor;
}

export function wallNow() {
  return Date.now() + COMPANY_OFFSET_MINUTES * MINUTE;
}

export function dayStart(wall) {
  return Math.floor(wall / DAY) * DAY;
}

/** yyyy-MM-dd of a wall time. */
export function day(wall) {
  return new Date(wall).toISOString().slice(0, 10);
}

/** yyyy-MM of a wall time. */
export function month(wall) {
  return day(wall).slice(0, 7);
}

/** The instant of a wall time, as the API takes it (ISO, UTC). */
export function utcIso(wall) {
  return new Date(wall - COMPANY_OFFSET_MINUTES * MINUTE).toISOString();
}

export function mondayOf(wall) {
  const start = dayStart(wall);
  return start - ((new Date(start).getUTCDay() + 6) % 7) * DAY;
}

export function quarterStart(wall) {
  const date = new Date(wall);
  return Date.UTC(date.getUTCFullYear(), Math.floor(date.getUTCMonth() / 3) * 3, 1);
}

export function monthStart(wall) {
  const date = new Date(wall);
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1);
}

/** A person's pause between actions, in seconds, scaled by THINK and the profile. Lets the live connection run meanwhile. */
export function pause(minSeconds, maxSeconds) {
  const seconds = (minSeconds + Math.random() * (maxSeconds - minSeconds)) * THINK * profileThink;
  return new Promise((resolve) => setTimeout(resolve, seconds * 1000));
}
