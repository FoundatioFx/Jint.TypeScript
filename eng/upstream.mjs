#!/usr/bin/env node
import { parseArgs } from 'node:util';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { check, refresh, newStage, prepareMerge, apply, rollback, importOrigin, verifyOrigin } from './upstream/workflow.mjs';
const root = fileURLToPath(new URL('..', import.meta.url));
try {
    const { values, positionals } = parseArgs({ allowPositionals: true, options: {
        'work-dir': { type: 'string' }, ref: { type: 'string' }, framework: { type: 'string' },
        'allow-normalizer-change': { type: 'boolean' }, help: { type: 'boolean' }
    } });
    const command = positionals[0] ?? 'status';
    if (values.help) {
        console.log('node eng/upstream.mjs status|check|refresh\nnode eng/upstream.mjs prepare|verify-origin --work-dir NEW_DIRECTORY [--ref HEAD] [--framework net8.0]\nnode eng/upstream.mjs apply|rollback --work-dir PREPARED_DIRECTORY\nUse prepare --ref PIN --allow-normalizer-change when intentionally changing normalization.');
    } else {
        if (positionals.length > 1) throw new Error('Expected one command');
        let result;
        if (command === 'status' || command === 'check') result = check(root);
        else if (command === 'refresh') result = refresh(root);
        else {
            if (!values['work-dir']) throw new Error('--work-dir is required');
            const stage = resolve(values['work-dir']);
            if (command === 'prepare' || command === 'verify-origin') {
                const allowNormalizerChange = values['allow-normalizer-change'] ?? false;
                check(root, allowNormalizerChange && command === 'prepare');
                newStage(root, stage);
                const pin = JSON.parse(readFileSync(resolve(root, 'eng/upstream.json'), 'utf8')).acornima.commit;
                const ref = command === 'verify-origin' ? pin : values.ref ?? 'HEAD';
                const { incoming, commit } = await importOrigin(root, stage, ref, values.framework);
                result = command === 'verify-origin' ? verifyOrigin(root, incoming, commit)
                    : prepareMerge(root, incoming, stage, commit, { allowNormalizerChange });
                if (result.conflicts?.length) process.exitCode = 2;
            } else if (command === 'apply') result = apply(root, stage);
            else if (command === 'rollback') result = rollback(root, stage);
            else throw new Error(`Unknown command: ${command}`);
        }
        console.log(JSON.stringify(result, null, 2));
    }
} catch (error) { console.error(error.message); process.exitCode = 1; }
