'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

for (const executable of ['powershell.exe', 'pwsh.exe']) {
  test(`GitHub bootstrap contracts (${executable})`, { skip: process.platform !== 'win32' }, t => {
    const env = { ...process.env };
    // Each PowerShell edition must discover its own modules, including Windows PowerShell 5.1.
    delete env.PSModulePath;
    const probe = spawnSync(executable, ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()'], { encoding: 'utf8', windowsHide: true, env });
    if (probe.error?.code === 'ENOENT') return t.skip(`${executable} is not installed`);
    assert.equal(probe.status, 0, probe.stderr);
    const script = path.resolve(__dirname, '../../scripts/test-bootstrap-install.ps1');
    const result = spawnSync(executable, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script], {
      encoding: 'utf8', windowsHide: true, timeout: 30000, env
    });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.match(result.stdout, /Bootstrap contracts passed/);
  });
}
