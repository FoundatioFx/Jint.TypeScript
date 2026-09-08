import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync, lstatSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { filesUnder } from '../parser-files.mjs';

export const paths = {
    parser: 'src/Jint.TypeScript/Parsing', base: 'eng/upstream/acornima',
    baseline: 'eng/upstream/baseline.json', owned: 'eng/upstream/owned-files.json',
    inventory: 'eng/upstream/customizations.json', patch: 'eng/parser.patch', pins: 'eng/upstream.json'
};
const normalizers = ['eng/import-parser.mjs', 'eng/parser-files.mjs', 'eng/AdaptAst/Program.cs', 'eng/AdaptAst/AdaptAst.csproj', 'global.json', 'Directory.Build.props'];
const tooling = [...normalizers, 'eng/upstream.mjs', 'eng/upstream/workflow.mjs'];
const json = path => JSON.parse(readFileSync(path, 'utf8'));
const save = (path, value) => { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, JSON.stringify(value, null, 2) + '\n'); };
const hash = value => createHash('sha256').update(value).digest('hex');
const equal = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const ensure = (condition, message) => { if (!condition) throw new Error(message); };
export function run(command, args, cwd, allowed = [0]) {
    const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('GIT_')));
    const result = spawnSync(command, args, { cwd, env, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, windowsHide: true });
    if (result.error) throw result.error;
    ensure(allowed.includes(result.status), `${command} ${args.join(' ')} failed (${result.status})\n${result.stdout}\n${result.stderr}`);
    return result.stdout;
}
export const git = (cwd, args, allowed) => run('git', ['-c', 'core.autocrlf=false', '-c', 'core.fileMode=false',
    '-c', 'core.hooksPath=/dev/null', '-c', 'core.excludesFile=/dev/null', '-c', 'init.templateDir=',
    '-c', 'merge.renames=true', '-c', 'merge.conflictStyle=diff3', '-c', 'rerere.enabled=false',
    '-c', 'commit.gpgsign=false', '-c', 'user.name=Jint upstream updater',
    '-c', 'user.email=upstream@localhost', ...args], cwd, allowed);
export function validName(name) {
    ensure(typeof name === 'string' && name.length > 0 && !isAbsolute(name) && !name.includes('\\')
        && !name.split('/').some(p => !p || p === '.' || p === '..' || p.toLowerCase() === '.git')
        && !/[:\x00-\x1f]/u.test(name), `Unsafe relative file path: ${name}`);
    return name;
}
export function tree(directory, excludeGit = false) {
    noSymlinkParents(directory);
    const names = filesUnder(directory, excludeGit ? new Set(['.git']) : new Set());
    ensure(new Set(names.map(name => name.toLowerCase())).size === names.length, 'Case-colliding file paths are not portable');
    return Object.fromEntries(names.map(name => [validName(name), hash(readFileSync(join(directory, name)))]));
}
function noSymlinkParents(path) {
    for (let current = resolve(path); ; current = dirname(current)) {
        if (existsSync(current)) ensure(!lstatSync(current).isSymbolicLink(), `Symbolic link is not allowed: ${current}`);
        if (dirname(current) === current) break;
    }
}
const fingerprint = (root, names) => Object.fromEntries(names.map(name => {
    const file = join(root, name); noSymlinkParents(file);
    return [name, hash(readFileSync(file))];
}));
function ownership(root) {
    const owned = json(join(root, paths.owned));
    ensure(Array.isArray(owned) && new Set(owned).size === owned.length, 'Owned-file manifest must contain unique paths');
    owned.forEach(validName);
    const base = tree(join(root, paths.base));
    for (const name of owned) ensure(!(name in base), `Owned file collides with upstream: ${name}`);
    const live = tree(join(root, paths.parser));
    for (const name of owned) ensure(name in live, `Owned file is missing: ${name}`);
    for (const name of Object.keys(live)) ensure(name in base || owned.includes(name), `Unclassified parser file: ${name}. Add owned helpers to ${paths.owned}.`);
    return { owned, base, live };
}
function verifyBaseline(root, allowNormalizerChange = false) {
    const receipt = json(join(root, paths.baseline));
    ensure(receipt.schema === 1, 'Unsupported baseline schema');
    ensure(receipt.commit === json(join(root, paths.pins)).acornima.commit, 'Baseline and Acornima pin disagree');
    ensure(equal(receipt.files, tree(join(root, paths.base))), 'Normalized baseline was modified');
    if (!allowNormalizerChange) ensure(equal(receipt.normalizers, fingerprint(root, normalizers)),
        'Normalizer changed. Reimport the pinned revision with prepare --allow-normalizer-change, review, then apply.');
}
function temporary(action) {
    const directory = mkdtempSync(join(tmpdir(), 'jint-upstream-'));
    try { return action(directory); } finally { rmSync(directory, { recursive: true, force: true }); }
}
function copyFiles(source, target, names) {
    mkdirSync(target, { recursive: true });
    for (const name of names) {
        validName(name);
        const destination = join(target, name);
        mkdirSync(dirname(destination), { recursive: true });
        cpSync(join(source, name), destination);
    }
}
function replaceFiles(source, target, previousNames, nextNames) {
    for (const name of previousNames) rmSync(join(target, validName(name)), { force: true });
    copyFiles(source, target, nextNames);
}
function patchAndInventory(root) {
    const { owned, base, live } = ownership(root);
    return temporary(stage => {
        copyFiles(join(root, paths.base), stage, Object.keys(base));
        git(stage, ['init', '--quiet']);
        git(stage, ['add', '--all']);
        const managed = Object.keys(live).filter(name => !owned.includes(name));
        replaceFiles(join(root, paths.parser), stage, Object.keys(base), managed);
        const patch = git(stage, ['diff', '--binary', '--no-ext-diff', '--no-textconv', '--no-renames', '--no-color', '--src-prefix=a/', '--dst-prefix=b/']);
        const changes = Object.keys(base).filter(name => base[name] !== live[name]).map(name => ({
            file: name, status: name in live ? 'modified' : 'deleted'
        }));
        const inventory = { schema: 1, upstreamFiles: Object.keys(base).length, customizedUpstreamFiles: changes,
            ownedFiles: owned, addedLines: (patch.match(/^\+(?!\+\+)/gmu) ?? []).length,
            removedLines: (patch.match(/^-(?!--)/gmu) ?? []).length, patchLines: (patch.match(/\n/gu) ?? []).length };
        return { patch, inventory };
    });
}
export function recordBaseline(root) {
    save(join(root, paths.baseline), { schema: 1, commit: json(join(root, paths.pins)).acornima.commit,
        normalizers: fingerprint(root, normalizers), files: tree(join(root, paths.base)) });
}
export function refresh(root) {
    verifyBaseline(root);
    const { patch, inventory } = patchAndInventory(root);
    writeFileSync(join(root, paths.patch), patch);
    save(join(root, paths.inventory), inventory);
    return inventory;
}
export function check(root, allowNormalizerChange = false) {
    verifyBaseline(root, allowNormalizerChange);
    const { owned, live } = ownership(root);
    temporary(stage => {
        cpSync(join(root, paths.base), stage, { recursive: true });
        git(stage, ['init', '--quiet']);
        if (readFileSync(join(root, paths.patch)).length) git(stage, ['apply', '--whitespace=nowarn', join(root, paths.patch)]);
        const managed = Object.fromEntries(Object.entries(live).filter(([name]) => !owned.includes(name)));
        ensure(equal(tree(stage, true), managed), 'Parser patch is stale; run node eng/upstream.mjs refresh after intentional parser changes');
    });
    const { inventory } = patchAndInventory(root);
    ensure(equal(inventory, json(join(root, paths.inventory))), 'Customization inventory is stale; run refresh');
    return inventory;
}
function state(root) {
    return { parser: tree(join(root, paths.parser)), base: tree(join(root, paths.base)),
        metadata: fingerprint(root, [paths.baseline, paths.owned, paths.inventory, paths.patch, paths.pins]), tools: fingerprint(root, tooling) };
}
export function newStage(root, stage) {
    root = resolve(root); stage = resolve(stage);
    noSymlinkParents(stage);
    // Staging may live under artifacts only, or outside the project. Never overlap source/tooling or an ancestor.
    const inside = relative(root, stage);
    ensure(inside && (inside.startsWith('artifacts/') || inside.startsWith('artifacts\\') || inside.split(/[\\/]/u)[0] === '..' || isAbsolute(inside)), 'Use artifacts/<new-directory> or a new directory outside the project');
    const projectFromStage = relative(stage, root);
    ensure(projectFromStage.split(/[\\/]/u)[0] === '..' || isAbsolute(projectFromStage), 'Staging cannot contain the project');
    ensure(!existsSync(stage), 'Staging directory already exists; use a new work directory');
    mkdirSync(stage, { recursive: true });
}
export function prepareMerge(root, incoming, stage, commit, { allowNormalizerChange = false } = {}) {
    root = resolve(root); incoming = resolve(incoming); stage = resolve(stage);
    const incomingFromStage = relative(stage, incoming);
    ensure(incomingFromStage && !isAbsolute(incomingFromStage) && !incomingFromStage.split(/[\\/]/u).includes('..'), 'Incoming baseline must be inside the staging directory');
    check(root, allowNormalizerChange);
    ensure(/^[0-9a-f]{40}$/u.test(commit), 'Expected an exact 40-character upstream commit');
    ensure(!existsSync(join(stage, 'receipt.json')) && !existsSync(join(stage, 'merge')), 'Stage already contains a prepared update');
    const before = state(root);
    const incomingFiles = tree(incoming);
    const owned = json(join(root, paths.owned));
    for (const name of owned) for (const upstreamName of Object.keys(incomingFiles)) {
        const a = name.toLowerCase(), b = upstreamName.toLowerCase();
        ensure(a !== b && !a.startsWith(b + '/') && !b.startsWith(a + '/'), `Incoming upstream collides with owned file: ${name}`);
    }
    const merge = join(stage, 'merge');
    copyFiles(join(root, paths.base), merge, Object.keys(before.base));
    git(merge, ['init', '--quiet', '--initial-branch=baseline']);
    git(merge, ['add', '--all']); git(merge, ['commit', '--quiet', '--allow-empty', '-m', 'Normalized old upstream']);
    const baseCommit = git(merge, ['rev-parse', 'HEAD']).trim();
    git(merge, ['checkout', '--quiet', '-b', 'customizations']);
    replaceFiles(join(root, paths.parser), merge, Object.keys(before.base), Object.keys(before.parser).filter(name => !owned.includes(name)));
    git(merge, ['add', '--all']); git(merge, ['commit', '--quiet', '--allow-empty', '-m', 'Current customizations']);
    git(merge, ['checkout', '--quiet', '-b', 'incoming', baseCommit]);
    replaceFiles(incoming, merge, Object.keys(tree(merge, true)), Object.keys(incomingFiles));
    git(merge, ['add', '--all']); git(merge, ['commit', '--quiet', '--allow-empty', '-m', `Acornima ${commit}`]);
    git(merge, ['checkout', '--quiet', 'customizations']);
    const mergeOutput = git(merge, ['merge', '--no-commit', '--no-ff', 'incoming'], [0, 1]);
    ensure(git(merge, ['rev-parse', 'MERGE_HEAD']).trim() === git(merge, ['rev-parse', 'incoming']).trim(), 'Git did not start the expected upstream merge');
    const conflicts = git(merge, ['diff', '--name-only', '--diff-filter=U']).trim().split('\n').filter(Boolean);
    const receipt = { schema: 1, root, commit, before, incomingFiles, incoming: relative(stage, incoming), status: 'prepared', conflicts };
    save(join(stage, 'receipt.json'), receipt);
    const upstreamDiff = git(merge, ['diff', '--stat', baseCommit, 'incoming']);
    writeFileSync(join(stage, 'upstream.diff'), git(merge, ['diff', '--no-ext-diff', '--no-textconv', baseCommit, 'incoming']));
    writeFileSync(join(stage, 'REPORT.md'), `# Acornima update\n\nCommit: ${commit}\n\nLive sources are unchanged. Review upstream.diff and merge/.\n\n${upstreamDiff}\n${mergeOutput}\nConflicts: ${conflicts.length}\n${conflicts.join('\n')}\n`);
    return { commit, stage, conflicts };
}
function readReceipt(root, stage) {
    const receipt = json(join(stage, 'receipt.json'));
    ensure(receipt.schema === 1 && receipt.root === resolve(root), 'Update belongs to a different project');
    ensure(receipt.incoming && !isAbsolute(receipt.incoming) && !receipt.incoming.split(/[\\/]/u).includes('..'), 'Invalid incoming path in receipt');
    return receipt;
}
const backedUp = [paths.parser, paths.base, paths.baseline, paths.owned, paths.inventory, paths.patch, paths.pins];
function restore(root, stage, expectedBackup) {
    ensure(equal(expectedBackup, tree(join(stage, 'backup'))), 'Backup changed; preserve current files and recover manually');
    for (const name of backedUp) {
        rmSync(join(root, name), { recursive: true, force: true });
        cpSync(join(stage, 'backup', name), join(root, name), { recursive: true });
    }
}
export function apply(root, stage) {
    const receipt = readReceipt(root, stage);
    ensure(receipt.status === 'prepared', 'Update is not in prepared state');
    ensure(equal(receipt.before, state(root)), 'Local work changed after prepare; preserve it and prepare again');
    const incoming = join(stage, receipt.incoming);
    ensure(equal(receipt.incomingFiles, tree(incoming)), 'Incoming baseline changed after prepare');
    const merge = join(stage, 'merge');
    ensure(!git(merge, ['ls-files', '--unmerged']).trim(), 'Unresolved merge conflicts; resolve and git add the paths in the staging repository');
    const candidate = tree(merge, true);
    const owned = json(join(root, paths.owned));
    for (const name of Object.keys(candidate)) {
        ensure(name in receipt.incomingFiles && !owned.includes(name), `Candidate file is not incoming upstream: ${name}. Review upstream renames/deletions explicitly.`);
        ensure(!/^(<<<<<<< |=======$|>>>>>>> )/mu.test(readFileSync(join(merge, name), 'utf8')), `Conflict marker remains in ${name}`);
    }
    ensure(!existsSync(join(stage, 'backup')), 'Backup already exists');
    for (const name of backedUp) { mkdirSync(dirname(join(stage, 'backup', name)), { recursive: true }); cpSync(join(root, name), join(stage, 'backup', name), { recursive: true }); }
    receipt.backup = tree(join(stage, 'backup'));
    receipt.status = 'applying'; save(join(stage, 'receipt.json'), receipt);
    try {
        replaceFiles(merge, join(root, paths.parser), Object.keys(receipt.before.parser).filter(name => !owned.includes(name)), Object.keys(candidate));
        rmSync(join(root, paths.base), { recursive: true }); cpSync(incoming, join(root, paths.base), { recursive: true });
        const pins = json(join(root, paths.pins));
        if (pins.acornima.commit !== receipt.commit) {
            pins.acornima.commit = receipt.commit;
            save(join(root, paths.pins), pins);
        }
        recordBaseline(root); refresh(root); check(root);
        receipt.after = state(root); receipt.status = 'applied'; save(join(stage, 'receipt.json'), receipt);
    } catch (error) {
        restore(root, stage, receipt.backup); receipt.status = 'restored'; save(join(stage, 'receipt.json'), receipt); throw error;
    }
    return { commit: receipt.commit, backup: join(stage, 'backup') };
}
export function rollback(root, stage) {
    const receipt = readReceipt(root, stage);
    ensure(receipt.status === 'applied', 'Automatic rollback requires a completed apply; for an interrupted apply preserve current files and recover from backup manually');
    ensure(equal(receipt.after, state(root)), 'Local work changed after apply; automatic rollback would overwrite it');
    restore(root, stage, receipt.backup); receipt.status = 'rolled-back'; save(join(stage, 'receipt.json'), receipt);
    return { restored: true };
}
export async function importOrigin(root, stage, ref = 'HEAD', framework = 'net8.0') {
    ensure(ref && !ref.startsWith('-') && !/[\s\x00]/u.test(ref), 'Invalid upstream ref');
    ensure(/^net\d+\.\d+$/u.test(framework), 'Invalid upstream target framework');
    const repository = json(join(root, paths.pins)).acornima.repository;
    const checkout = join(stage, 'checkout'), generated = join(stage, 'generated'), incoming = join(stage, 'incoming');
    console.log(`Fetching ${repository} (${ref}) into ${stage}`);
    run('git', ['clone', '--quiet', '--no-checkout', '--filter=blob:none', repository, checkout], root);
    git(checkout, ['fetch', '--quiet', 'origin', ref]);
    const commit = git(checkout, ['rev-parse', 'FETCH_HEAD^{commit}']).trim();
    git(checkout, ['checkout', '--quiet', '--detach', commit]);
    console.log(`Building upstream generators at ${commit}`);
    // Run from our root so our SDK selection applies, not a potentially unavailable upstream SDK.
    run('dotnet', ['build', join(checkout, 'src/Acornima/Acornima.csproj'), '-c', 'Release', '-f', framework,
        '-p:EmitCompilerGeneratedFiles=true', `-p:CompilerGeneratedFilesOutputPath=${generated}`], root);
    run(process.execPath, [join(root, 'eng/import-parser.mjs'), checkout, generated, incoming], root);
    run('dotnet', ['run', '--project', join(root, 'eng/AdaptAst'), '-c', 'Release', '--', incoming], root);
    return { incoming, commit };
}
export function verifyOrigin(root, incoming, commit) {
    ensure(commit === json(join(root, paths.pins)).acornima.commit, 'Origin commit differs from pin');
    ensure(equal(tree(incoming), tree(join(root, paths.base))), 'Fresh upstream import differs from the stored normalized baseline');
    verifyBaseline(root);
    return { commit, reproducible: true };
}
