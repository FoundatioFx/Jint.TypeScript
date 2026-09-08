// Compatibility entry point: the checked-in normalized baseline is now authoritative.
import { fileURLToPath } from 'node:url';
import { check, refresh } from './upstream/workflow.mjs';
const root = fileURLToPath(new URL('..', import.meta.url));
const args = process.argv.slice(2);
if (args.length !== 1 || !['--check', '--write'].includes(args[0])) {
    console.error('Usage: node eng/parser-delta.mjs --check|--write\nThe baseline is stored in eng/upstream/acornima; use node eng/upstream.mjs for upgrades.');
    process.exitCode = 1;
} else {
    try { console.log(JSON.stringify(args[0] === '--check' ? check(root) : refresh(root), null, 2)); }
    catch (error) { console.error(error.message); process.exitCode = 1; }
}
