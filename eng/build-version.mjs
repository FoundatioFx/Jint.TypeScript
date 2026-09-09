import { appendFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

// Match the shared Foundatio workflow's primary and maintenance branches.
export function branchSuffix(ref) {
    if (!ref.startsWith('refs/heads/')) return '';
    const name = ref.slice('refs/heads/'.length);
    if (name === 'main' || name === 'master' || /^\d+\.(?:x|\d+)$/.test(name)) return '';
    // A single nonnumeric SemVer identifier also handles slashes and punctuation.
    return `${name.replace(/[^0-9A-Za-z-]/g, '-').replace(/^(?=\d+$)/, 'branch-')}.`;
}

const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;

export function buildVersion(version, ref) {
    if (ref.startsWith('refs/tags/')) {
        if (!ref.startsWith('refs/tags/v') || !semver.test(ref.slice('refs/tags/v'.length)))
            throw new Error('A release tag must be v followed by a valid SemVer version.');
        // MinVer can select another tag on the same commit. The triggering tag wins.
        return ref.slice('refs/tags/v'.length);
    }
    if (!semver.test(version)) throw new Error(`MinVer returned an invalid version: ${version}`);
    const suffix = branchSuffix(ref);
    const [number, metadata] = version.split('+');
    const branch = suffix.slice(0, -1);
    if (!suffix || number.split('-').slice(1).join('-').split('.').slice(1).includes(branch)) return version;
    // A feature branch can inherit a prerelease tag without our branch identifier.
    const result = number.includes('-') ? number.replace(/\.(\d+)$/, `.${suffix}$1`) : `${number}-preview.${suffix}0`;
    if (result === number) return `${number}.${branch}${metadata ? `+${metadata}` : ''}`;
    return `${result}${metadata ? `+${metadata}` : ''}`;
}

function output(file, text) {
    if (file) appendFileSync(file, `${text}\n`);
}

function main() {
    const ref = process.env.GITHUB_REF ?? '';
    if (process.argv[2] === 'reason') {
        const suffix = branchSuffix(ref);
        output(process.env.GITHUB_ENV, `GIT_BRANCH_SUFFIX=${suffix}`);
        console.log(`branch: ${suffix} ref: ${ref} event: ${process.env.GITHUB_EVENT_NAME ?? ''} actor: ${process.env.GITHUB_ACTOR ?? ''}`);
    } else if (process.argv[2] === 'version') {
        const result = spawnSync('minver', ['--tag-prefix', 'v', '--minimum-major-minor', '0.1',
            '--default-pre-release-identifiers', `preview.${branchSuffix(ref)}0`], { encoding: 'utf8' });
        if (result.error) throw result.error;
        if (result.status !== 0) throw new Error(`MinVer failed (${result.status}): ${result.stderr}`);
        const version = buildVersion(result.stdout.trim(), ref);
        output(process.env.GITHUB_ENV, `MINVERVERSIONOVERRIDE=${version}`);
        output(process.env.GITHUB_STEP_SUMMARY, `### Version: ${version}`);
        console.log(`Version: ${version}`);
    } else throw new Error('Usage: node eng/build-version.mjs reason|version');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
