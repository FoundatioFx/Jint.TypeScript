// Deterministic grammar combinations and invalid mutations, independent of the C# parser.
// A fixed seed and committed outputs make failures reproducible without Node in CI.
export function hardeningCases(parse) {
  let seed = 0x21243889;
  const next = n => {
    seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5;
    return (seed >>> 0) % n;
  };
  const leaves = ['number', 'string', 'boolean', 'void', 'null', 'undefined', 'unknown',
    'any', 'never', 'symbol', 'bigint', 'object', 'true', 'false', '42', '42n', '"😀"', 'Domain.Result'];
  function type(depth) {
    if (!depth) return leaves[next(leaves.length)];
    switch (next(5)) {
      case 0: return `(${type(depth - 1)})[]`;
      case 1: return `(${type(depth - 1)} | ${type(depth - 1)})`;
      case 2: return `Result<${type(depth - 1)}, ${type(depth - 1)},>`;
      case 3: return `Array<${type(depth - 1)}>`;
      default: return leaves[next(leaves.length)];
    }
  }
  const contexts = [
    t => `let value: ${t} = 42; value;`,
    t => `const {x: value = 42}: ${t} = {}; value;`,
    t => `let [value = 42]: ${t} = []; value;`,
    t => `function f(value: ${t} = 42): ${t} { return value; } f();`,
    t => `function f(...values: ${t}): ${t} { return values[0]; } f(42);`,
    t => `const f = (value: ${t}): ${t} => value; f(42);`,
    t => `const f = (value): ${t} => value; f(42);`,
    t => `const f = (): ${t} => 42; f();`,
    t => `const f = async (value: ${t} = 42): Promise<${t}> => value; f();`,
    t => `const o = { ['f'](value: ${t}): ${t} { return value; } }; o.f(42);`,
    t => `class C { static f(value: ${t}): ${t} { return value; } } C.f(42);`,
    t => `class C { get value(): ${t} { return 42; } } new C().value;`,
    t => `class C { set value(value: ${t}) { this.result = value; } } const c = new C(); c.value = 42; c.result;`,
    t => `function* f(value: ${t}): ${t} { yield value; } f(42).next().value;`,
    t => `let value = 0; for (const x: ${t} of [20, 22]) value += x; value;`,
    t => `const f = true ? (value): ${t} => value : (value: ${t}) => 0; f(42);`,
    t => `const f = async (value): Promise<${t}> => value; f(42);`,
    t => `function f({x: [value = 42]}: ${t} = {x: []}): ${t} { return value; } f();`,
    t => `const f = ([value = 42]: ${t} = []): ${t} => value; f();`,
    t => `const o = { async f(value: ${t}): Promise<${t}> { return value; } }; o.f(42);`
  ];
  const cases = [];
  for (let i = 0; i < 1200; i++) {
    const annotation = type(1 + next(4));
    let source = contexts[i % contexts.length](annotation);
    if (i % 3 === 0) source = source.replaceAll(': ', ': /* erased 😀 : => */\r\n ');
    if (i % 5 === 0) source = source.replaceAll(' | ', ' /* union */ |\n ');
    cases.push({ name: `generated-${i}`, source });
  }
  for (const [i, source] of [
    'export function f(x: number): number { return x; }',
    'export default (x: Array<number>): number => x[0];',
    'export const f = async (x): Promise<number> => x;',
    'import { value as other } from "values"; export const value: number = other;',
    'export default class C { f(x: number): number { return x; } }'
  ].entries()) cases.push({ name: `module-${i}`, source, module: true });

  const invalid = new Set();
  const tokens = ['number', 'Array', '<', '>', '[', ']', '|', '(', ')', ',', ':', '?', '=', '=>', ';'];
  for (let i = 0; i < 4000; i++) {
    const original = 'Array < ( number | null ) [ ] >'.split(' ');
    const at = next(original.length);
    if (i % 2) original.splice(at, 1);
    else original.splice(at, 0, tokens[next(tokens.length)]);
    const source = `const value: ${original.join(' ')} = 42;`;
    try { parse(source, { sourceType: 'script', plugins: ['typescript'] }); }
    catch { invalid.add(source); }
  }
  return { cases, invalid: [...invalid].sort().map((source, i) => ({ name: `invalid-mutation-${i}`, source })) };
}
