import { readFileSync, readdirSync, lstatSync } from 'node:fs';
import { join } from 'node:path';

export function filesUnder(directory, excludedDirectories = new Set()) {
    if (lstatSync(directory).isSymbolicLink()) throw new Error(`Symbolic link is not allowed: ${directory}`);
    const files = [];
    function visit(relative) {
        for (const entry of readdirSync(join(directory, relative), { withFileTypes: true })) {
            const name = relative ? `${relative}/${entry.name}` : entry.name;
            if (entry.isSymbolicLink()) throw new Error(`Symbolic link is not allowed: ${name}`);
            if (entry.isDirectory() && !excludedDirectories.has(entry.name)) visit(name);
            else if (entry.isFile()) files.push(name);
        }
    }
    visit('');
    return files.sort();
}

// Match the upstream import's text normalization on Windows and Unix.
export function readSource(path) {
    return readFileSync(path, 'utf8').replace(/^\uFEFF/u, '').replace(/\r\n?/gu, '\n');
}
