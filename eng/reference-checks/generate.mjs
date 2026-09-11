import { parse } from '@babel/parser';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { hardeningCases } from './hardening-cases.mjs';
import { milestoneCases } from './milestone-cases.mjs';
import { parityCases } from './parity-cases.mjs';
import { genericsCases } from './generics-cases.mjs';
import { signatureCases } from './signature-cases.mjs';
import { typeSyntaxCases } from './type-syntax-cases.mjs';
import { classErasureCases } from './class-erasure-cases.mjs';
import { typeMemberCases } from './type-member-cases.mjs';

// Development only. Babel checks the selected syntax; current tsc supplies an
// independent JS emission oracle. Committed fixtures run without Node/npm/tsc.
const types = ['any', 'unknown', 'number', 'string', 'boolean', 'object', 'bigint', 'symbol',
  'undefined', 'never', 'void', 'null', 'true', 'false', '42', '"literal"', '42n',
  'Result', 'My.Domain.Result', 'number[]', '(number | string)[]',
  'Array<Array<number>>', 'Map<string, Array<number | null>>',
  'Result<A, B,>', '| number | string', 'number\n | string', 'number /* : => */ []'];
const contexts = [
  t => `let x: ${t} = 42; x;`,
  t => `function f(x: ${t}): ${t} { return x; } f(42);`,
  t => `const f = (x: ${t}): ${t} => x; f(42);`,
  t => `const f = (x): ${t} => x; f(42);`,
  t => `const f = (x: ${t} = 42) => x; f();`,
  t => `const o = { f(x: ${t}): ${t} { return x; } }; o.f(42);`,
  t => `const f = async (x: ${t}): Promise<${t}> => x; f(42);`
];
const cases = types.flatMap((type, i) => contexts.map((context, j) => ({ name: `type-${i}-context-${j}`, source: context(type) })));
cases.push(
  { name: 'negation', source: 'function f(x: boolean): boolean { return !x; } f(false);' },
  { name: 'division-and-regex', source: 'const x: number = 4; /a/.test("a") ? x / 2 : 0;' },
  { name: 'destructuring', source: 'function f({x}: Result, [y]: Array<number>): number { return x + y; } f({x: 40}, [2]);' },
  { name: 'conditional-return-type', source: 'const f = true ? (x): number => x + 1 : (x): number => 0; f(41);' },
  { name: 'ordinary-conditional', source: 'const a = 40, b = 2; false ? (a) : b / 2;' },
  { name: 'default-arrow-nesting', source: 'const f = (x: number = ((y: number): number => y)(42)) => x; f();' },
  { name: 'imports', module: true, source: 'import { foo as bar } from "values"; export const result: number = bar + 2;' },
  // Ported from acorn-typescript 8956dc5 __test__/arrow-function/type.test.ts.
  // Its MIT notice is retained in THIRD-PARTY-NOTICES.txt.
  { name: 'acorn-ts-assignment-pattern', source: '(x = 42): void => {}' },
  { name: 'acorn-ts-issue-32', source: 'const testApp = async(app: string, index: number) => {};' },
  { name: 'acorn-ts-issue-38', module: true, source: 'let defaultHashSize = 0; export const getHashPlaceholderGenerator = (): any => { let nextIndex = 0; return (optionName: string, hashSize: number = defaultHashSize) => {}; };' },
  { name: 'acorn-ts-issue-39', module: true, source: 'export const getPureFunctions = ({ treeshake }: NormalizedInputOptions): PureFunctions => {};' }
);

const temporary = mkdtempSync(join(tmpdir(), 'jint-ts-reference-'));
const hardening = hardeningCases(parse);
cases.push(...hardening.cases);
const milestone = milestoneCases(parse);
cases.push(...milestone.cases);
hardening.invalid.push(...milestone.invalid);
const parity = parityCases(parse);
cases.push(...parity.cases);
hardening.invalid.push(...parity.invalid);
const generics = genericsCases(parse);
cases.push(...generics.cases);
hardening.invalid.push(...generics.invalid);
const signatures = signatureCases(parse);
cases.push(...signatures.cases);
hardening.invalid.push(...signatures.invalid);
const typeSyntax = typeSyntaxCases(parse);
cases.push(...typeSyntax.cases);
hardening.invalid.push(...typeSyntax.invalid);
const classErasure = classErasureCases(parse);
cases.push(...classErasure.cases);
hardening.invalid.push(...classErasure.invalid);
const typeMembers = typeMemberCases(parse);
cases.push(...typeMembers.cases);
hardening.invalid.push(...typeMembers.invalid);
try {
  mkdirSync(join(temporary, 'input'));
  for (const test of cases) {
    // TypeScript 7 always emits strict mode. Make it explicit in both inputs;
    // default Jint script/sloppy semantics have their own JavaScript regression corpus.
    if (!test.module) test.source = '"use strict";\n' + test.source;
    let babelError;
    try { parse(test.source, { sourceType: test.module ? 'module' : 'script', plugins: ['typescript'] }); }
    catch (error) { babelError = error; }
    if (test.babelUnsupported ? !babelError : babelError)
      throw new Error(`${test.name}: ${test.source}`, {cause: babelError});
    writeFileSync(join(temporary, 'input', `${test.name}.ts`), test.source);
  }
  writeFileSync(join(temporary, 'tsconfig.json'), JSON.stringify({
    compilerOptions: { target: 'ESNext', module: 'ESNext', noCheck: true,
      verbatimModuleSyntax: true, rootDir: 'input', outDir: 'output',
      removeComments: true }, include: ['input/*.ts']
  }));
  const tsc = fileURLToPath(new URL('./node_modules/typescript/bin/tsc', import.meta.url));
  execFileSync(process.execPath, [tsc, '--project', join(temporary, 'tsconfig.json')], { stdio: 'inherit' });
  for (const test of cases) test.javascript = readFileSync(join(temporary, 'output', `${test.name}.js`), 'utf8');
  const output = fileURLToPath(new URL('../../tests/Jint.TypeScript.Tests/Fixtures/typescript.json', import.meta.url));
  writeFileSync(output, JSON.stringify({ babel: '8.0.4', typescript: '7.0.2', cases }, null, 2));
  writeFileSync(fileURLToPath(new URL('../../tests/Jint.TypeScript.Tests/Fixtures/invalid-typescript.json', import.meta.url)),
    JSON.stringify({ babel: '8.0.4', cases: hardening.invalid }, null, 2));
  console.log(`Generated ${cases.length} tsc-emitted reference cases (${cases.filter(c=>!c.babelUnsupported).length} Babel-accepted) at ${resolve(output)}`);
  console.log(`Generated ${hardening.invalid.length} Babel-rejected syntax mutations`);
} finally { rmSync(temporary, { recursive: true, force: true }); }
