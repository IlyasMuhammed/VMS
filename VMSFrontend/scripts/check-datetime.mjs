// Runs the date helper tests once per time zone, so a helper that only works in one zone cannot pass.
// The zones are the awkward ones: UTC, the client's (UTC+5), the far ends of the map (UTC-11, UTC+14), the
// two sides of a DST change in the US and Europe, and a half-hour offset (India, UTC+5:30).
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const zones = ['UTC', 'Asia/Karachi', 'Pacific/Pago_Pago', 'Pacific/Kiritimati', 'America/Los_Angeles', 'Europe/London', 'Asia/Kolkata'];
const testFile = fileURLToPath(new URL('./check-datetime.test.mjs', import.meta.url));

let failed = 0;
for (const zone of zones) {
  const run = spawnSync(process.execPath, ['--test', testFile], {
    env: { ...process.env, TZ: zone, NODE_NO_WARNINGS: '1' },
    encoding: 'utf8',
  });
  const summary = /# pass (\d+)[\s\S]*# fail (\d+)/.exec(run.stdout);
  const passed = summary ? summary[1] : '?';
  const failures = summary ? Number(summary[2]) : NaN;
  const ok = run.status === 0 && failures === 0;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${zone.padEnd(22)} ${passed} passed`);
  if (!ok) {
    failed++;
    console.log(run.stdout + run.stderr);
  }
}

if (failed) {
  console.error(`\n${failed} of ${zones.length} time zones failed.`);
  process.exit(1);
}
console.log(`\nDate helpers behave the same in all ${zones.length} time zones.`);
