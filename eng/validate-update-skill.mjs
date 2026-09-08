import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, resolve } from 'node:path';
const root = fileURLToPath(new URL('..', import.meta.url));
const directory = process.argv[2] ? resolve(process.argv[2]) : join(root, '.agents/skills/jint-update-acornima');
const skill = readFileSync(join(directory, 'SKILL.md'), 'utf8');
const frontmatter = skill.match(/^---\nname: ([a-z0-9-]+)\ndescription: ("[^\n]+")\n---\n/u);
assert.ok(frontmatter, 'Expected a name and quoted YAML description');
assert.equal(frontmatter[1], 'jint-update-acornima');
const description = JSON.parse(frontmatter[2]);
assert.ok(description.length > 20 && description.length <= 1024 && !/[<>]/u.test(description));
const ui = readFileSync(join(directory, 'agents/openai.yaml'), 'utf8');
for (const key of ['display_name', 'short_description', 'default_prompt']) {
    const line = ui.match(new RegExp(`^  ${key}: ("[^\\n]+")$`, 'mu'));
    assert.ok(line, `Missing quoted ${key}`);
    const value = JSON.parse(line[1]);
    if (key === 'short_description') assert.ok(value.length >= 25 && value.length <= 64);
    if (key === 'default_prompt') assert.ok(value.includes('$jint-update-acornima'));
}
for (const file of ['docs/upstream-updates.md', 'docs/maintenance.md', 'docs/upstream-customization-audit.md', 'eng/upstream.mjs']) {
    assert.ok(skill.includes(file), `Skill must route to ${file}`);
    assert.ok(existsSync(join(root, file)), `Missing resource: ${file}`);
}
console.log(`Validated ${directory}`);
