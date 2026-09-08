import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { apply, check, git, newStage, paths, prepareMerge, recordBaseline, refresh, rollback, run, tree, validName, verifyOrigin } from '../upstream/workflow.mjs';
const project = fileURLToPath(new URL('../..', import.meta.url));
const original = Array.from({ length: 30 }, (_, i) => `line ${i}`).join('\n') + '\n';
const oldCommit = '1'.repeat(40), newCommit = '2'.repeat(40);
const write = (path, value) => { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, value); };
const read = path => readFileSync(path, 'utf8');
function fixture(t) {
    const temp = mkdtempSync(join(tmpdir(), 'jint-update-test-'));
    t.after(() => rmSync(temp, { recursive: true, force: true }));
    const root = join(temp, 'project'), stage = join(temp, 'stage');
    for (const file of ['eng/import-parser.mjs', 'eng/parser-files.mjs', 'eng/AdaptAst/Program.cs', 'eng/AdaptAst/AdaptAst.csproj',
        'global.json', 'Directory.Build.props', 'eng/upstream.mjs', 'eng/upstream/workflow.mjs'])
        write(join(root, file), read(join(project, file)));
    write(join(root, paths.base, 'Parser.cs'), original);
    write(join(root, paths.parser, 'Parser.cs'), original.replace('line 2\n', 'custom line 2\n'));
    write(join(root, paths.parser, 'Parser.Custom.cs'), 'owned partial\r\n');
    write(join(root, paths.owned), JSON.stringify(['Parser.Custom.cs']));
    write(join(root, paths.pins), JSON.stringify({ acornima: { commit: oldCommit, repository: 'unused', package: '1.0' }, jint: { package: 'keep' } }));
    recordBaseline(root); refresh(root);
    function prepare(edit = () => {}, commit = newCommit) {
        newStage(root, stage);
        const incoming = join(stage, 'incoming'); cpSync(join(root, paths.base), incoming, { recursive: true });
        edit(incoming);
        return prepareMerge(root, incoming, stage, commit);
    }
    return { root, stage, prepare, live: join(root, paths.parser, 'Parser.cs'), own: join(root, paths.parser, 'Parser.Custom.cs') };
}

test('clean merge preserves customizations, owned bytes, package pins and project Git index; rollback restores everything', t => {
    const f = fixture(t);
    git(f.root, ['init', '--quiet']); git(f.root, ['add', '--all']);
    const index = readFileSync(join(f.root, '.git/index'));
    write(join(f.root, 'untracked-user-work.txt'), 'preserve');
    const before = tree(f.root, true), owned = readFileSync(f.own);
    const result = f.prepare(dir => write(join(dir, 'Parser.cs'), original.replace('line 25\n', 'upstream line 25\n')));
    assert.deepEqual(result.conflicts, []); assert.deepEqual(tree(f.root, true), before);
    apply(f.root, f.stage);
    assert.match(read(f.live), /custom line 2/); assert.match(read(f.live), /upstream line 25/);
    assert.deepEqual(readFileSync(f.own), owned); assert.deepEqual(readFileSync(join(f.root, '.git/index')), index);
    const pins = JSON.parse(read(join(f.root, paths.pins)));
    assert.equal(pins.acornima.commit, newCommit); assert.equal(pins.acornima.package, '1.0'); assert.equal(pins.jint.package, 'keep');
    check(f.root); rollback(f.root, f.stage); assert.deepEqual(tree(f.root, true), before);
});

test('no-op update retains the exact source and patch bytes', t => {
    const f = fixture(t), before = tree(f.root);
    f.prepare(() => {}, oldCommit); apply(f.root, f.stage); assert.deepEqual(tree(f.root), before);
});

test('overlapping changes remain staged until conflicts are resolved and added', t => {
    const f = fixture(t), before = tree(f.root);
    assert.deepEqual(f.prepare(dir => write(join(dir, 'Parser.cs'), original.replace('line 2\n', 'upstream line 2\n'))).conflicts, ['Parser.cs']);
    assert.throws(() => apply(f.root, f.stage), /Unresolved merge conflicts/);
    assert.deepEqual(tree(f.root), before);
    const merged = join(f.stage, 'merge/Parser.cs'); write(merged, original.replace('line 2\n', 'combined line 2\n'));
    assert.throws(() => apply(f.root, f.stage), /Unresolved merge conflicts/);
    git(join(f.stage, 'merge'), ['add', '--', 'Parser.cs']); apply(f.root, f.stage);
    assert.match(read(f.live), /combined line 2/); check(f.root);
});

test('upstream renames carry local edits; additions and deletions are represented in the next baseline', t => {
    const f = fixture(t);
    write(join(f.root, paths.base, 'Removed.cs'), 'remove\n'); write(join(f.root, paths.parser, 'Removed.cs'), 'remove\n');
    recordBaseline(f.root); refresh(f.root);
    f.prepare(dir => { renameSync(join(dir, 'Parser.cs'), join(dir, 'Renamed.cs')); rmSync(join(dir, 'Removed.cs')); write(join(dir, 'Added.cs'), 'new\n'); });
    apply(f.root, f.stage);
    assert.equal(existsSync(f.live), false); assert.match(read(join(f.root, paths.parser, 'Renamed.cs')), /custom line 2/);
    assert.equal(existsSync(join(f.root, paths.parser, 'Removed.cs')), false);
    assert.equal(read(join(f.root, paths.parser, 'Added.cs')), 'new\n'); check(f.root);
});

test('upstream deletion of a customized file requires explicit resolution', t => {
    const f = fixture(t);
    assert.deepEqual(f.prepare(dir => rmSync(join(dir, 'Parser.cs'))).conflicts, ['Parser.cs']);
    assert.throws(() => apply(f.root, f.stage), /Unresolved merge conflicts/);
    git(join(f.stage, 'merge'), ['rm', '--', 'Parser.cs']); apply(f.root, f.stage);
    assert.equal(existsSync(f.live), false); check(f.root);
});

test('intentional local upstream-file deletion survives an unrelated update', t => {
    const f = fixture(t); rmSync(f.live); refresh(f.root);
    f.prepare(dir => write(join(dir, 'Added.cs'), 'new\n')); apply(f.root, f.stage);
    assert.equal(existsSync(f.live), false); check(f.root);
});

for (const file of ['Parser.cs', 'Parser.Custom.cs']) test(`changed live ${file} blocks apply without losing work`, t => {
    const f = fixture(t); f.prepare(); const target = join(f.root, paths.parser, file); write(target, 'new user work');
    assert.throws(() => apply(f.root, f.stage), /Local work changed/); assert.equal(read(target), 'new user work');
});

test('new upstream cannot collide with an owned partial', t => {
    const f = fixture(t), before = tree(f.root);
    assert.throws(() => f.prepare(dir => write(join(dir, 'Parser.Custom.cs'), 'collision')), /collides with owned/);
    assert.deepEqual(tree(f.root), before);
});

test('changed incoming snapshot blocks apply', t => {
    const f = fixture(t); f.prepare(); write(join(f.stage, 'incoming/Parser.cs'), 'tampered');
    assert.throws(() => apply(f.root, f.stage), /Incoming baseline changed/);
});

test('a staged conflict marker blocks apply even after git add', t => {
    const f = fixture(t); f.prepare(); write(join(f.stage, 'merge/Parser.cs'), '<<<<<<< ours\n');
    git(join(f.stage, 'merge'), ['add', '--all']); assert.throws(() => apply(f.root, f.stage), /Conflict marker/);
});

test('new unclassified candidate files block apply', t => {
    const f = fixture(t); f.prepare(); write(join(f.stage, 'merge/Unexpected.cs'), 'extra');
    assert.throws(() => apply(f.root, f.stage), /not incoming upstream/);
});

test('candidate symlinks block apply', t => {
    const f = fixture(t); f.prepare(); symlinkSync(f.own, join(f.stage, 'merge/Link.cs'));
    assert.throws(() => apply(f.root, f.stage), /Symbolic link/);
});

test('baseline corruption cannot be refreshed away', t => {
    const f = fixture(t); write(join(f.root, paths.base, 'Parser.cs'), 'corrupt');
    assert.throws(() => check(f.root), /baseline was modified/); assert.throws(() => refresh(f.root), /baseline was modified/);
});

test('normalizer changes require explicit reimport and are captured by apply', t => {
    const f = fixture(t); write(join(f.root, 'eng/AdaptAst/Program.cs'), 'changed normalizer');
    assert.throws(() => check(f.root), /Normalizer changed/); assert.throws(() => refresh(f.root), /Normalizer changed/);
    check(f.root, true); newStage(f.root, f.stage);
    const incoming = join(f.stage, 'incoming'); cpSync(join(f.root, paths.base), incoming, { recursive: true });
    prepareMerge(f.root, incoming, f.stage, oldCommit, { allowNormalizerChange: true }); apply(f.root, f.stage); check(f.root);
});

test('changed tooling after prepare blocks apply', t => {
    const f = fixture(t); f.prepare(); write(join(f.root, 'eng/upstream.mjs'), 'changed tooling');
    assert.throws(() => apply(f.root, f.stage), /Local work changed/);
});

test('unclassified live files are rejected until ownership is explicit', t => {
    const f = fixture(t); write(join(f.root, paths.parser, 'NewPartial.cs'), 'new helper');
    assert.throws(() => refresh(f.root), /Unclassified parser file/);
    write(join(f.root, paths.owned), JSON.stringify(['Parser.Custom.cs', 'NewPartial.cs'])); refresh(f.root); check(f.root);
});

test('stale patch is detected and refresh is deterministic', t => {
    const f = fixture(t); write(f.live, 'new customization'); assert.throws(() => check(f.root), /patch is stale/);
    refresh(f.root); const patch = read(join(f.root, paths.patch)); refresh(f.root); assert.equal(read(join(f.root, paths.patch)), patch); check(f.root);
});

test('rollback refuses to overwrite later local work', t => {
    const f = fixture(t); f.prepare(); apply(f.root, f.stage); write(f.own, 'new user work');
    assert.throws(() => rollback(f.root, f.stage), /Local work changed/); assert.equal(read(f.own), 'new user work');
});

test('rollback checks backup integrity before deleting any current files', t => {
    const f = fixture(t); f.prepare(); apply(f.root, f.stage); const before = tree(f.root);
    write(join(f.stage, 'backup', paths.parser, 'Parser.cs'), 'tampered backup');
    assert.throws(() => rollback(f.root, f.stage), /Backup changed/); assert.deepEqual(tree(f.root), before);
});

test('double apply and rollback without apply are rejected', t => {
    const f = fixture(t); f.prepare(); assert.throws(() => rollback(f.root, f.stage), /completed apply/);
    apply(f.root, f.stage); assert.throws(() => apply(f.root, f.stage), /not in prepared state/);
});

test('reproducible-origin check detects both changed bytes and wrong revisions', t => {
    const f = fixture(t); f.prepare(); const incoming = join(f.stage, 'incoming');
    assert.equal(verifyOrigin(f.root, incoming, oldCommit).reproducible, true);
    assert.throws(() => verifyOrigin(f.root, incoming, newCommit), /commit differs/);
    write(join(incoming, 'Parser.cs'), 'changed'); assert.throws(() => verifyOrigin(f.root, incoming, oldCommit), /differs from the stored/);
});

test('staging rejects source directories, ancestors, symlinks and existing directories', t => {
    const f = fixture(t);
    for (const stage of [f.root, dirname(f.root), join(f.root, 'src/new'), join(f.root, 'eng/new'), join(f.root, '..hidden/new')]) assert.throws(() => newStage(f.root, stage));
    newStage(f.root, f.stage); assert.throws(() => newStage(f.root, f.stage), /already exists/);
    symlinkSync(f.stage, join(f.root, 'linked-stage'), 'dir'); assert.throws(() => newStage(f.root, join(f.root, 'linked-stage/new')), /Symbolic link/);
});

test('unsafe owned manifest paths are rejected', t => {
    const f = fixture(t);
    for (const name of ['../outside', '/absolute', 'x\\y', 'x/../y', '.git/config', 'c:drive', 'x//y']) assert.throws(() => validName(name), /Unsafe/);
    write(join(f.root, paths.owned), JSON.stringify(['../outside'])); assert.throws(() => refresh(f.root), /Unsafe/);
});

test('importer refuses a nonempty output without overwriting user files', t => {
    const f = fixture(t); newStage(f.root, f.stage); write(join(f.stage, 'user.txt'), 'keep');
    assert.throws(() => run(process.execPath, [join(project, 'eng/import-parser.mjs'), 'missing', 'missing', f.stage], project), /new or empty directory/);
    assert.equal(read(join(f.stage, 'user.txt')), 'keep');
});

for (const name of ['parser.custom.cs', 'Parser.Custom.cs/Nested.cs']) test(`owned collision guard also catches ${name}`, t => {
    const f = fixture(t), before = tree(f.root);
    assert.throws(() => f.prepare(dir => write(join(dir, name), 'collision')), /collides with owned/);
    assert.deepEqual(tree(f.root), before);
});

test('wrong project cannot apply another project update', t => {
    const first = fixture(t), second = fixture(t); first.prepare();
    assert.throws(() => apply(second.root, first.stage), /different project/);
});

test('receipt traversal cannot redirect the incoming baseline', t => {
    const f = fixture(t); f.prepare(); const path = join(f.stage, 'receipt.json');
    const receipt = JSON.parse(read(path)); receipt.incoming = '../outside'; write(path, JSON.stringify(receipt));
    assert.throws(() => apply(f.root, f.stage), /Invalid incoming path/);
});
