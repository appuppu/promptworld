// TAC reachability CLI — thin wrapper over server/tacreach.js. Usage:
//   cat server/tacsim.js server/tacreach.js scripts/tac-reach.js | node - stage1.json [stage2.json ...]
// Exit 0 = every stage PASSes (all intel / medkits / exit walk-reachable).
// The same analyzer gates create_stage/update_stage in the worker.
'use strict';
var fs = require('fs');

var files = process.argv.slice(2);
var allOk = true;
files.forEach(function (f) {
  var stage = JSON.parse(fs.readFileSync(f, 'utf8'));
  var r = tacAnalyzeReachability(stage);
  var label = f.split('/').pop();
  console.log('[' + (r.pass ? 'PASS' : 'FAIL') + '] ' + label +
    '  jump-only tops(optional): ' + r.jumpTops + '  dead tops: ' + r.deadTops +
    (r.skipped ? '  (SKIPPED: over work budget)' : ''));
  r.failures.forEach(function (s) { console.log('    ' + s); });
  r.warnings.forEach(function (s) { console.log('    ' + s); });
  if (!r.pass) allOk = false;
});
console.log(allOk ? 'REACHABILITY: ALL PASS' : 'REACHABILITY: FAILURES');
process.exit(allOk ? 0 : 1);
