import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { convertJsDoc } from '../src/jsdoc-converter.mjs';

async function convert(source, extra = {}) {
    const files = { 'main.js': source, ...extra };
    const before = JSON.stringify(files);
    const result = await convertJsDoc(files, 'main.js');
    assert.equal(JSON.stringify(files), before, 'conversion must not mutate its input');
    return result;
}
const successes = [
    ['parameters and returns', '/** @param {number} n\n * @returns {number} */\nexport function double(n) { return n * 2; }', 'n: number'],
    ['optional parameters', '/** @param {string} [name] */\nexport function greet(name) { return name ?? "world"; }\ngreet();', 'name?: string'],
    ['default parameters', '/** @param {string} [name] */\nexport function greet(name = "world") { return name; }\ngreet();', 'name: string ='],
    ['equals optional types', '/** @param {string=} name */\nexport function greet(name) { return name ?? "world"; }\ngreet();', 'name?:'],
    ['generics', '/** @template T\n * @param {T} value\n * @returns {T} */\nexport function identity(value) { return value; }', 'identity<T>'],
    ['variable types', '/** @type {string | number} */\nexport let value = 42;', 'value: string'],
    ['rest parameters', '/** @param {...number} values */\nexport function sum(...values) { return values.reduce((a,b) => a+b, 0); }', '...values: number[]'],
    ['destructuring', '/** @param {{name: string}} input */\nexport function greet({name}) { return name; }', ': { name: string; }'],
    ['ordinary JavaScript', 'export const answer = 42;', 'answer = 42'],
    ['unicode and CRLF', '// 😀\r\n/** @param {number} n */\r\nexport function double(n) { return n * 2; }', 'n: number'],
    ['comments and strings mentioning tags', 'export const text = "@callback @enum"; // @callback\n/** Documentation about callbacks. */\nexport const n = 1;', '@callback @enum'],
];
for (const [name, source, expected] of successes) test(`converts ${name}`, async () => {
    const result = await convert(source);
    assert.equal(result.success, true, JSON.stringify(result));
    assert.equal(result.file, 'main.ts');
    assert.ok(result.files['main.ts'].includes(expected), result.files['main.ts']);
    assert.equal(Object.hasOwn(result.files, 'main.js'), false);
});
const failures = [
    ['callbacks', '/** @callback Mapper\n * @param {string} value\n * @returns {number} */\nexport const n = 1;', '@callback'],
    ['casts', '/** @param {unknown} value */\nexport function f(value) { return (/** @type {string} */ (value)).length; }', 'casts'],
    ['satisfies', '/** @satisfies {{n: number}} */\nexport const value = {n:1};', '@satisfies'],
    ['class inheritance', '/** @extends {Array<string>} */\nexport class Strings extends Array {}', '@extends'],
    ['syntax errors', 'export function f( {', 'syntax errors'],
    ['type errors', '/** @param {number} n */\nexport function f(n) { return n.toUpperCase(); }', 'needs attention'],
    ['implicit any', 'export function f(n) { return n; }', 'needs attention'],
    ['variadic without rest syntax', '/** @param {...number} n */\nexport function f(n) { return n; }', 'rest parameter'],
    ['multiple declarations with one type', '/** @type {number} */\nexport let a = 1, b = 2;', 'separate variables'],
];
for (const [name, source, expected] of failures) test(`leaves ${name} unchanged`, async () => {
    const result = await convert(source);
    assert.equal(result.success, false);
    assert.ok(result.issues[0].message.includes(expected), JSON.stringify(result));
    assert.equal(Object.hasOwn(result, 'files'), false);
});
test('does not overwrite an existing TypeScript file', async () => {
    const result = await convert('export const n = 1;', { 'main.ts': 'export const original = true;' });
    assert.equal(result.success, false);
    assert.match(result.issues[0].message, /already exists/);
});
test('bounds source size', async () => {
    const result = await convert(' '.repeat(100_001));
    assert.equal(result.success, false);
    assert.match(result.issues[0].message, /limits/);
});
test('converts the actual sample one file at a time with JSDoc imports intact', async () => {
    let files = Object.fromEntries(['main', 'quote', 'subtotal'].map(name => [`migration/${name}.js`, readFileSync(new URL(`../../Scripts/migration/${name}.js`, import.meta.url), 'utf8')]));
    files['host.d.ts'] = readFileSync(new URL('../../Scripts/host.d.ts', import.meta.url), 'utf8');
    for (const name of ['subtotal', 'quote', 'main']) {
        const result = await convertJsDoc(files, `migration/${name}.js`);
        assert.equal(result.success, true, JSON.stringify(result));
        files = result.files;
    }
    assert.match(files['migration/subtotal.ts'], /export (type|interface) CartLine/);
    assert.match(files['migration/quote.ts'], /export (type|interface) QuoteInput/);
    assert.match(files['migration/main.ts'], /['"]\.\/quote\.ts['"]/);
    assert.match(files['migration/quote.ts'], /['"]\.\/subtotal\.ts['"]/);
    assert.ok(!Object.keys(files).some(name => name.endsWith('.js')));
});

test('updates module literals without rewriting strings or ordinary comments', async () => {
    const files = {
        'lib/value.js': '/** @typedef {{n: number}} Value */\nexport const value = {n: 42};',
        'main.js': `import {value} from './lib/value.js';
export {value as other} from './lib/value.js';
/** @type {import('./lib/value.js').Value} */
const copy = value;
export async function run() { return (await import('./lib/value.js')).value; }
export const text = './lib/value.js'; // './lib/value.js'
export const n = copy.n;`,
        'types.ts': "export type Value = import('./lib/value.js').Value;"
    };
    const result = await convertJsDoc(files, 'lib/value.js');
    assert.equal(result.success, true, JSON.stringify(result));
    assert.equal((result.files['main.js'].match(/\.\/lib\/value\.ts/g) ?? []).length, 4);
    assert.ok(result.files['types.ts'].includes('./lib/value.ts'));
    assert.ok(result.files['main.js'].includes("export const text = './lib/value.js'; // './lib/value.js'"));
});
test('supports encoded filenames and parent-relative imports', async () => {
    const result = await convertJsDoc({ 'space name.js': 'export const n = 42;', 'lib/main.js': "import {n} from '../space%20name.js'; export function run() { return n; }" }, 'space name.js');
    assert.equal(result.success, true, JSON.stringify(result));
    assert.ok(result.files['lib/main.js'].includes('../space%20name.ts'));
});
test('keeps explicit TypeScript callers checked against converted parameters', async () => {
    const result = await convert('/** @param {number} n */\nexport function double(n) { return n*2; }', {
        'caller.ts': "import {double} from './main.js'; export const n: number = double(21);"
    });
    assert.equal(result.success, true, JSON.stringify(result));
    assert.match(result.files['caller.ts'], /['"]\.\/main\.ts['"]/);
});
for (const [source, message] of [
    ['/** @param {string} unused */\nexport const n = 1;', 'not attached'],
    ['/** @property {string} unused */\nexport const n = 1;', '@typedef'],
    ['/** @template T */\nexport function f() { return 1; }', 'template'],
    ['export const path = "./main.js"; export async function f() { return import(path); }', 'literal paths'],
]) test(`does not silently lose unsupported annotations: ${message}`, async () => {
    const result = await convert(source);
    assert.equal(result.success, false);
    assert.ok(result.issues[0].message.includes(message), JSON.stringify(result));
});
for (const [name, source] of [
    ['arrows', '/** @param {number} value\n * @returns {number} */\nexport const f = value => value*2;'],
    ['function expressions', '/** @param {number} value */\nexport const f = function(value) { return value*2; };'],
    ['nullable types', '/** @param {?string} value */\nexport function f(value) { return value ?? "none"; }'],
    ['optional object properties', '/** @typedef {{n: number, name?: string}} Value */\n/** @param {Value} value */\nexport function f(value) { return value.name ?? String(value.n); }'],
    ['local typedefs', 'export function f() {\n/** @typedef {number} Value */\n/** @type {Value} */\nconst n = 42; return n; }'],
]) test(`converts ${name} with unchanged runtime code`, async () => {
    const result = await convert(source);
    assert.equal(result.success, true, JSON.stringify(result));
});

test('refuses adjacent JSDoc blocks the upstream action cannot convert', async () => {
    const result = await convert('/** @typedef {{n: number}} Value */\n/** @type {Value} */\nexport const value = {n: 42};');
    assert.equal(result.success, false);
    assert.match(result.issues[0].message, /could not be converted/);
});
