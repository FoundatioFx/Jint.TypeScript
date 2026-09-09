import { test } from 'node:test';
import assert from 'node:assert/strict';
import { branchSuffix, buildVersion } from '../build-version.mjs';

test('primary, maintenance, pull request and tag refs use the standard Foundatio version', () => {
    for (const ref of ['refs/heads/main', 'refs/heads/master', 'refs/heads/1.x', 'refs/heads/7.10', 'refs/pull/42/merge', 'refs/tags/v0.1.0'])
        assert.equal(branchSuffix(ref), '');
    assert.equal(buildVersion('0.1.0-preview.0.9', 'refs/heads/main'), '0.1.0-preview.0.9');
    assert.equal(buildVersion('0.1.0-preview.0.9', 'refs/pull/42/merge'), '0.1.0-preview.0.9');
});

test('feature branch names form valid nonnumeric prerelease identifiers', () => {
    assert.equal(branchSuffix('refs/heads/my-feature'), 'my-feature.');
    assert.equal(branchSuffix('refs/heads/feature/imports'), 'feature-imports.');
    assert.equal(branchSuffix('refs/heads/fix_v1.2'), 'fix-v1-2.');
    assert.equal(branchSuffix('refs/heads/007'), 'branch-007.');
});

test('preserves MinVer branch versions and inserts the branch into inherited prerelease versions', () => {
    const ref = 'refs/heads/feature/imports';
    assert.equal(buildVersion('0.1.0-preview.feature-imports.0.9', ref), '0.1.0-preview.feature-imports.0.9');
    assert.equal(buildVersion('0.1.0-preview.1.9', ref), '0.1.0-preview.1.feature-imports.9');
    assert.equal(buildVersion('0.1.0-preview.1.feature-imports.9', ref), '0.1.0-preview.1.feature-imports.9');
    assert.equal(buildVersion('0.1.0-preview.1.9+build.42', ref), '0.1.0-preview.1.feature-imports.9+build.42');
});

test('a branch named preview does not collide with the main version', () => {
    assert.equal(buildVersion('0.1.0-preview.0.9', 'refs/heads/preview'), '0.1.0-preview.0.preview.9');
    assert.equal(buildVersion('0.1.0-preview.preview.0.9', 'refs/heads/preview'), '0.1.0-preview.preview.0.9');
});

test('feature branches at a version tag still receive a branch version', () => {
    assert.equal(buildVersion('0.1.0', 'refs/heads/feature'), '0.1.0-preview.feature.0');
    assert.equal(buildVersion('0.1.0-rc', 'refs/heads/feature'), '0.1.0-rc.feature');
});

test('the triggering tag supplies the exact stable or prerelease version, even with multiple tags on one commit', () => {
    assert.equal(buildVersion('0.2.0', 'refs/tags/v0.1.0'), '0.1.0');
    assert.equal(buildVersion('0.1.0', 'refs/tags/v0.1.0-preview.1'), '0.1.0-preview.1');
    assert.equal(buildVersion('0.1.1-preview.0', 'refs/tags/v0.1.0-rc.2+build.42'), '0.1.0-rc.2+build.42');
});

test('invalid release tags fail instead of publishing an unrelated MinVer version', () => {
    for (const tag of ['version', 'v1', 'v1.2', 'v01.2.3', 'v1.2.3-preview.01', 'v1.2.3-foo..bar', 'v1.2.3-', 'v1.2.3+'])
        assert.throws(() => buildVersion('0.1.0-preview.0.9', `refs/tags/${tag}`), /valid SemVer/);
});

test('invalid MinVer output fails before writing a version override', () => {
    for (const version of ['', 'latest', 'v1.2.3', '1.2.3\nINJECTED=value'])
        assert.throws(() => buildVersion(version, 'refs/heads/main'), /invalid version/);
});
