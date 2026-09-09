import { readdirSync, statSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';

// Never select an arbitrary stale version when a local output directory contains several builds.
export function findPackage(input = 'artifacts/packages') {
    let path = resolve(input);
    if (statSync(path).isDirectory()) {
        const packages = readdirSync(path).filter(name => /^Jint\.TypeScript\..+\.nupkg$/.test(name));
        if (packages.length !== 1)
            throw new Error(`Expected one Jint.TypeScript package in ${path}; found ${packages.length}. Pass an explicit .nupkg path or use an empty output directory.`);
        path = join(path, packages[0]);
    }
    const version = /^Jint\.TypeScript\.(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.nupkg$/.exec(basename(path))?.[1];
    if (!version || !statSync(path).isFile()) throw new Error('Pass a Jint.TypeScript .nupkg filename or its directory.');
    return { path, version };
}
