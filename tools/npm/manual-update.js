'use strict';

const { spawn } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const readline = require('node:readline');
const path = require('node:path');
const { readOperation, waitForOperation } = require('./startup-updates');

function eligible(args) {
  const values = [];
  const locales = ['--locale', '--idioma', '--langue', '--lingua'];
  for (let index = 0; index < args.length; index++) {
    const value = args[index].toLowerCase();
    if (locales.includes(value)) {
      if (++index >= args.length || args[index].startsWith('-')) return false;
    } else if (!locales.some(option => value.startsWith(option + '='))) values.push(args[index]);
  }
  return values.length === 1 && ['update', '-update', '--update'].includes(values[0]);
}

async function runManual(dll, args, supplied = {}) {
  const options = { spawn, process, stdout: process.stdout, stderr: process.stderr,
    now: Date.now, sleep: ms => new Promise(resolve => setTimeout(resolve, ms)), readOperation, ...supplied };
  const nonce = randomUUID().replaceAll('-', '');
  const marker = `OMNICORE_UPDATE_${nonce}:`;
  const cancellation = new AbortController();
  options.signal = cancellation.signal;
  let receipt;
  const child = options.spawn('dotnet', [dll, ...args], {
    stdio: ['inherit', 'pipe', 'inherit'], windowsHide: true,
    env: { ...options.process.env, OMNICORE_UPDATE_RECEIPT_NONCE: nonce }
  });
  const handlers = new Map();
  for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
    const handler = () => {
      cancellation.abort();
      if (child.exitCode === null) { try { child.kill(signal); } catch { } }
    };
    options.process.on(signal, handler); handlers.set(signal, handler);
  }
  try {
    const lines = readline.createInterface({ input: child.stdout, crlfDelay: Infinity });
    let invalid = false;
    lines.on('line', line => {
      if (!line.startsWith(marker)) { options.stdout.write(line + '\n'); return; }
      try {
        const value = JSON.parse(line.slice(marker.length));
        if (receipt || line.length > 65536 || !/^[a-f0-9]{32}$/i.test(value.operationId ?? '')
            || typeof value.statePath !== 'string' || !path.isAbsolute(value.statePath)
            || !['waiting', 'succeeded', 'failed'].every(key => typeof value[key] === 'string'
              && value[key].length < 4096 && !/[\x00-\x1f\x7f]/.test(value[key]))) throw new Error('invalid receipt');
        receipt = value;
      } catch { invalid = true; }
    });
    const code = await new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', (exit, signal) => resolve(signal === 'SIGINT' ? 130 : signal ? 1 : exit ?? 1));
    });
    if (code !== 0) return code;
    if (cancellation.signal.aborted) return 130;
    if (invalid) return 1;
    if (!receipt) return 0;
    options.stdout.write(receipt.waiting + '\n');
    try {
      // Core must exit before its helper replaces DLLs. Observe the exact durable
      // operation here; never retry npm or the helper after an uncertain result.
      await waitForOperation(receipt, options);
      options.stdout.write(receipt.succeeded + '\n');
      return 0;
    } catch {
      if (cancellation.signal.aborted) return 130;
      options.stderr.write(receipt.failed + '\n');
      return 1;
    }
  } finally {
    for (const [signal, handler] of handlers) options.process.removeListener(signal, handler);
  }
}

module.exports = { eligible, runManual };
