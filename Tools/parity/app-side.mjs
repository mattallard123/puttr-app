// The app's side of the parity check: runs puttr-app's own GameFunctions.js and
// RolloutFunctions.js (imported straight from that repo, unchanged) over every putt in
// putts.json and writes what they make of each one to app.json.
//
//   node Tools/parity/app-side.mjs <putts.json> <app.json>
import fs from 'fs';
import path from 'path';
import { pathToFileURL } from 'url';

const here = path.dirname(new URL(import.meta.url).pathname);
const common = path.resolve(here, '../../../puttr-app/components/common');
// The app's files are ES modules saved as .js in a React Native project: copy them beside
// this script as .mjs so Node loads them as they are.
const work = path.join(here, '.app-copy');
fs.mkdirSync(work, { recursive: true });
for (const name of ['GameFunctions', 'RolloutFunctions']) {
  fs.copyFileSync(path.join(common, name + '.js'), path.join(work, name + '.mjs'));
}
const G = await import(pathToFileURL(path.join(work, 'GameFunctions.mjs')).href);
const R = await import(pathToFileURL(path.join(work, 'RolloutFunctions.mjs')).href);

const [input, output] = process.argv.slice(2);
const putts = JSON.parse(fs.readFileSync(input, 'utf8'));
const out = putts.map((p) => {
  const bp = typeof p.ballPath === 'string' ? JSON.parse(p.ballPath) : p.ballPath;
  const hb = typeof p.holeBounds === 'string' ? JSON.parse(p.holeBounds) : p.holeBounds;
  const d = Number(p.distance) || 6, tee = Number(p.tee) || 2;
  const c = G.getMakeProbability(bp, hb);
  const he = G.getHoleEntry(bp, hb);
  const te = G.getTeeEntry(d, tee);
  const madeForGames = c > 1;
  const analysis = R.analyzeRollout(bp, hb);
  const rollout = R.projectRollout(analysis, null, madeForGames);
  // Free putt's own score (games/Open.js processBallPath).
  const teScore = c > 1 ? te : 0;
  const angDiff = c > 0 ? he[2] - teScore : 0;
  const score = R.getPuttScoreRollout(angDiff, c > 0 ? 1 : 0, rollout, 0);
  const sl = R.getStartLine(bp, hb, d);
  return {
    id: p.id,
    ball: G.isBall(bp, hb),
    rollUnder: G.wasRollUnder(bp, hb),
    count: c,
    entry: he[2],
    teeLine: te,
    description: G.getEntryDescription(madeForGames ? he[2] - te : 0),
    missResult: G.getMissResult(bp, hb),
    missSide: madeForGames ? null : G.getMissSide(bp, hb),
    rollout,
    arrival: R.arrivalSpeed(analysis),
    score,
    grade: G.getPuttGrade(score),
    startLine: sl.ok ? sl.deg : null,
  };
});
fs.writeFileSync(output, JSON.stringify(out));
console.log('app side: ' + out.length + ' putts');
