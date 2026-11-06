'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const path = require('node:path');
const { runManual, eligible } = require('./manual-update');
const { run } = require('./launcher');

function harness(status = 'Succeeded', exit = 0, sendReceipt = true) {
  const parent = new EventEmitter(); parent.env = {};
  const output = []; const errors = []; const calls = []; let closed = false;
  const options = { process: parent, stdout: { write: text => output.push(text) },
    stderr: { write: text => errors.push(text) },
    spawn: (command, args, settings) => {
      calls.push({ command, args, settings });
      const child = new EventEmitter(); child.stdout = new PassThrough(); child.exitCode = null;
      child.kill = () => {};
      setImmediate(() => {
        child.stdout.write('Comprobando releases…\n');
        if (sendReceipt) child.stdout.write(`OMNICORE_UPDATE_${settings.env.OMNICORE_UPDATE_RECEIPT_NONCE}:` + JSON.stringify({
          operationId: 'b'.repeat(32), statePath: path.resolve('private-state.json'), version: '0.2.0',
          waiting: 'Instalando…', succeeded: 'Actualizado a 0.2.0.', failed: 'No se confirmó.'
        }) + '\n');
        child.stdout.end();
        child.exitCode = exit; closed = true; child.emit('close', exit);
      }); return child;
    },
    readOperation: () => {
      assert.equal(closed, true, 'must release Core DLL before waiting');
      return { schemaVersion: 1, operationId: 'b'.repeat(32), status };
    }
  };
  return { options, parent, output, errors, calls };
}

test('only the simple command and typo aliases use the completing wrapper', () => {
  for (const args of [['update'], ['-update'], ['--update'], ['--locale', 'fr', 'update'],
    ['update', '--idioma', 'es'], ['--LANGUE=fr', 'update'], ['update', '--lingua=it']]) assert.equal(eligible(args), true);
  for (const args of [[], ['ask', 'update'], ['update', 'apply'], ['update', 'check'], ['update', '--bogus'],
    ['update', '--locale'], ['--locale', '--update'], ['--', 'update']]) assert.equal(eligible(args), false);
});

test('waits after Core exits, observes exact operation and prints confirmed success', async () => {
  const h = harness();
  assert.equal(await runManual('/omni.dll', ['update'], h.options), 0);
  assert.deepEqual(h.calls[0].args, ['/omni.dll', 'update']);
  assert.equal(h.calls[0].settings.windowsHide, true);
  assert.deepEqual(h.output, ['Comprobando releases…\n', 'Instalando…\n', 'Actualizado a 0.2.0.\n']);
  assert.deepEqual(h.errors, []);
  assert.equal(h.parent.listenerCount('SIGINT'), 0);
});

test('failed or uncertain helper never reports success or retries installation', async () => {
  for (const status of ['Failed', 'Unknown']) {
    const h = harness(status);
    assert.equal(await runManual('/omni.dll', ['update'], h.options), 1);
    assert.deepEqual(h.errors, ['No se confirmó.\n']);
    assert.equal(h.calls.length, 1); assert.equal(h.output.some(x => x.startsWith('Actualizado')), false);
  }
});

test('already current performs no wait, and a failing Core exit is propagated', async () => {
  for (const exit of [0, 1, 2]) {
    const h = harness('Succeeded', exit, false);
    h.options.readOperation = () => assert.fail('must not read update state');
    assert.equal(await runManual('/omni.dll', ['update'], h.options), exit);
  }
});

test('a different durable operation never confirms this update', async () => {
  const h = harness();
  h.options.readOperation = () => ({ schemaVersion: 1, operationId: 'a'.repeat(32), status: 'Succeeded' });
  assert.equal(await runManual('/omni.dll', ['update'], h.options), 1);
  assert.equal(h.calls.length, 1);
  assert.deepEqual(h.errors, ['No se confirmó.\n']);
});

test('Ctrl+C stops waiting without relaunching or killing the detached installer', async () => {
  const h = harness('Running');
  h.options.sleep = async () => h.parent.emit('SIGINT');
  assert.equal(await runManual('/omni.dll', ['update'], h.options), 130);
  assert.equal(h.calls.length, 1);
  assert.deepEqual(h.errors, []);
  assert.equal(h.parent.listenerCount('SIGINT'), 0);
});

test('public launcher connects the simple command to completion in the same terminal', async () => {
  const h = harness();
  await run(['update'], { existsSync: () => true, process: h.parent, spawn: h.options.spawn,
    stderr: h.options.stderr, manualUpdateOptions: h.options });
  assert.equal(h.parent.exitCode, 0);
  assert.equal(h.calls.length, 1);
  assert.equal(h.calls[0].command, 'dotnet');
});
