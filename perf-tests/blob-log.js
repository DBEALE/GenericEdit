// Reads BlobStore.log and returns entries after a given timestamp.
// Used to isolate blob activity that occurred during a specific scenario.

const fs = require('fs');
const path = require('path');

const LOG_PATH = path.resolve(__dirname, '../backend/src/DatasetPlatform.Api/BlobStore.log');

/**
 * Returns all log entries after `sinceTime` (a Date), parsed into structured objects.
 */
function readEntriesSince(sinceTime) {
  if (!fs.existsSync(LOG_PATH)) return [];

  const lines = fs.readFileSync(LOG_PATH, 'utf8').split('\n').map(l => l.trimEnd()).filter(Boolean);

  return lines
    .map(parseLine)
    .filter((e) => e !== null && e.time > sinceTime);
}

/**
 * Parses a single log line into { time, impl, method, path, detail, ms }.
 * Line format: [yyyy-MM-dd HH:mm:ss.fff +00:00] ImplName.Method  key=val ... NNms
 */
function parseLine(line) {
  const m = line.match(/^\[(.+?)\] (\w+)\.(GetBlob|PutBlob|BlobExists|DeleteBlob|QueryBlobs)\s+(.+)\s+(\d+)ms$/);
  if (!m) return null;
  return {
    time:   new Date(m[1]),
    impl:   m[2],
    method: m[3],
    detail: m[4].trim(),
    ms:     parseInt(m[5], 10),
  };
}

/**
 * Summarises a set of log entries into a compact report.
 */
function summarise(entries) {
  if (entries.length === 0) return '  (no blob activity recorded)';

  const byMethod = {};
  for (const e of entries) {
    byMethod[e.method] = (byMethod[e.method] ?? []);
    byMethod[e.method].push(e);
  }

  const lines = [];
  let totalMs = 0;

  for (const [method, group] of Object.entries(byMethod)) {
    const total = group.reduce((s, e) => s + e.ms, 0);
    totalMs += total;
    lines.push(`  ${method.padEnd(12)} x${group.length}  (${total}ms total)`);
    // Show individual paths for reads/exists so we can spot redundancy
    if (method === 'GetBlob' || method === 'BlobExists') {
      for (const e of group) {
        lines.push(`    ${e.ms.toString().padStart(4)}ms  ${e.detail}`);
      }
    }
    if (method === 'QueryBlobs') {
      for (const e of group) {
        lines.push(`    ${e.ms.toString().padStart(4)}ms  ${e.detail}`);
      }
    }
  }

  lines.push(`  ─────────────────────────────────`);
  lines.push(`  Total blob ops: ${entries.length}  wall-clock blob time: ${totalMs}ms`);

  return lines.join('\n');
}

/**
 * Waits until the log file has not been modified for `idleMs` milliseconds,
 * indicating the backend has finished writing blob entries.
 * Times out after `timeoutMs`.
 */
async function waitUntilIdle(idleMs = 1500, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;
  let lastMtime = 0;
  let stableFor = 0;

  while (Date.now() < deadline) {
    await new Promise((r) => setTimeout(r, 200));
    const mtime = fs.existsSync(LOG_PATH) ? fs.statSync(LOG_PATH).mtimeMs : 0;
    if (mtime === lastMtime) {
      stableFor += 200;
      if (stableFor >= idleMs) return;
    } else {
      lastMtime = mtime;
      stableFor = 0;
    }
  }
}

module.exports = { readEntriesSince, summarise, waitUntilIdle };
