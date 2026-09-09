import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { findPackage } from '../package-file.mjs';

function directory(t) {
    const path = mkdtempSync(join(tmpdir(), 'jint-package-test-'));
    t.after(() => rmSync(path, { recursive: true, force: true }));
    return path;
}

test('discovers a versioned package without including symbols or consumer directories', t => {
    const root = directory(t);
    const name = 'Jint.TypeScript.0.1.0-preview.0.42.nupkg';
    writeFileSync(join(root, name), '');
    writeFileSync(join(root, name.replace('.nupkg', '.snupkg')), '');
    mkdirSync(join(root, 'consumer-123'));
    assert.deepEqual(findPackage(root), { path: join(root, name), version: '0.1.0-preview.0.42' });
});

test('rejects empty and ambiguous directories instead of testing a stale package', t => {
    const root = directory(t);
    assert.throws(() => findPackage(root), /found 0/);
    for (const version of ['0.1.0-preview.0.1', '0.1.0-preview.0.2'])
        writeFileSync(join(root, `Jint.TypeScript.${version}.nupkg`), '');
    assert.throws(() => findPackage(root), /found 2/);
    const path = join(root, 'Jint.TypeScript.0.1.0-preview.0.2.nupkg');
    assert.equal(findPackage(path).version, '0.1.0-preview.0.2');
});

test('rejects other packages, malformed filenames and directories masquerading as packages', t => {
    const root = directory(t);
    for (const name of ['Other.1.0.0.nupkg', 'Jint.TypeScript.latest.nupkg', 'Jint.TypeScript.1.0.0.snupkg']) {
        const path = join(root, name);
        writeFileSync(path, '');
        assert.throws(() => findPackage(path), /Pass a Jint.TypeScript/);
    }
    const path = join(directory(t), 'Jint.TypeScript.1.0.0.nupkg');
    mkdirSync(path);
    assert.throws(() => findPackage(path), /found 0/);
});
