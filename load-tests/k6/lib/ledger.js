import { RUN_ID } from './config.js';

// What the run changed, one LEDGER line per successful change, so verification can compare the database with it
// afterwards. The runner sends k6's console to a file (--console-output) and the tool reads these lines.

export function ledger(kind, fields) {
  console.log(`LEDGER ${JSON.stringify({ run: RUN_ID, kind, t: Date.now(), ...fields })}`);
}

/** Text that marks a record as made by this run, e.g. "[LT run 123]". */
export const runMark = `[LT run ${RUN_ID}]`;
