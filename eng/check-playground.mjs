import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';

// Exercise published output from an unrelated directory, with optional native-runtime selection.
const directory = resolve(process.argv[2] ?? 'artifacts/playground');
const child = spawn(process.env.PLAYGROUND_DOTNET ?? 'dotnet',
    [resolve(directory, 'Jint.TypeScript.Sample.dll'), '--urls', 'http://127.0.0.1:0'],
    { cwd: tmpdir(), stdio: ['ignore', 'pipe', 'pipe'] });
let output = '';
let timer;
try {
    const url = await new Promise((resolveUrl, reject) => {
        timer = setTimeout(() => reject(new Error(`Sample startup timed out.\n${output}`)), 20_000);
        child.on('error', reject);
        child.on('exit', code => reject(new Error(`Sample exited (${code}).\n${output}`)));
        child.stderr.on('data', data => output += data.toString());
        child.stdout.on('data', data => {
            output += data.toString();
            const match = output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
            if (match) resolveUrl(match[1]);
        });
    });
    clearTimeout(timer);
    const home = await fetch(url);
    assert.equal(home.status, 200);
    const html = await home.text();
    assert.match(html, /Jint.TypeScript Playground/);
    const asset = html.match(/src="([^\"]+\.js)"/)[1];
    assert.equal((await fetch(new URL(asset, url))).status, 200);
    const examples = await (await fetch(`${url}/api/examples`)).json();
    assert.equal(examples.length, 5);
    for (const example of examples) {
        const response = await fetch(`${url}/api/run`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Playground': '1' },
            body: JSON.stringify({ files: example.files, entryFile: example.entryFile, input: example.input }) });
        assert.equal(response.status, 200);
        const result = await response.json();
        assert.equal(result.success, true, JSON.stringify(result));
        assert.ok(result.logs.length);
        if (example.id === 'pricing') assert.equal(result.result.totalCents, 10625);
        if (example.id === 'migration') assert.deepEqual(result.result, { subtotalCents: 6500, discountCents: 650, totalCents: 5850 });
    }
    const blocked = await fetch(`${url}/api/run`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ files: {}, entryFile: 'main.ts', input: null }) });
    assert.equal(blocked.status, 400);
    console.log(`Published playground passed: HTML, assets, five examples, host logs, request header (${directory}).`);
} finally {
    clearTimeout(timer);
    if (child.exitCode === null && child.pid) {
        const exited = once(child, 'exit');
        child.kill();
        const force = setTimeout(() => child.kill('SIGKILL'), 3000);
        await exited;
        clearTimeout(force);
    }
}
