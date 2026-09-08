import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync } from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { filesUnder } from './parser-files.mjs';
const source = fileURLToPath(new URL('../skills/jint-update-acornima', import.meta.url));
const skills = resolve(process.argv[2] ?? join(process.env.CODEX_HOME ?? join(homedir(), '.codex'), 'skills'));
const destination = join(skills, 'jint-update-acornima');
try {
    if (process.argv.length > 3) throw new Error('Usage: node eng/install-update-skill.mjs [SKILLS_DIRECTORY]');
    if (existsSync(destination)) {
        const sourceFiles = filesUnder(source), installedFiles = filesUnder(destination);
        if (JSON.stringify(sourceFiles) !== JSON.stringify(installedFiles)
            || sourceFiles.some(name => !readFileSync(join(source, name)).equals(readFileSync(join(destination, name)))))
            throw new Error(`Existing skill differs: ${destination}. Review and preserve any local edits before replacing it from ${source}.`);
        console.log(`Already installed: ${destination}`);
    } else {
        mkdirSync(skills, { recursive: true });
        const staged = mkdtempSync(join(skills, '.jint-update-install-'));
        try { cpSync(source, staged, { recursive: true }); renameSync(staged, destination); }
        finally { rmSync(staged, { recursive: true, force: true }); }
        console.log(`Installed: ${destination}\nInvoke $jint-update-acornima. Open a new task if the skill list has not refreshed.`);
    }
} catch (error) { console.error(error.message); process.exitCode = 1; }
