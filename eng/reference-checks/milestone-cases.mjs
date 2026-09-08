// Independent Babel syntax checks and tsc emission cover the richer erased grammar.
export function milestoneCases(parse) {
  const types = [
    '{}', '{ value: number }', '{ readonly value?: number; name: string }',
    '{ [key: string]: number }', '{ readonly [index: number]: string; }',
    '{ "a-b"?: number, 42: string, default: boolean }',
    '{ value: { nested: [number, string?] }; next?: Result<number> }',
    '{ f(value: number, extra?: string): number; optional?(...xs: number[]): void }',
    '{ (value: number): string; new (value: number): Result }',
    '{ readonly(): number; readonly: number; readonly value?: number }',
    '{ <T extends Result<number> = Result<number>>(value: T): T }',
    '{ method<T,>(value: T): T; new<T>(value: T): Result<T> }',
    '(value: number) => string', '() => void', '(number) => number',
    '(this: Result, value?: number, ...rest: string[]) => Result',
    '<T extends Result<number> = Result<number>,>(value: T) => T',
    'new (value: number) => Result', 'new <T>(value: T) => Result<T>',
    '[]', '[number, string]', '[number?, string?]', '[first: number, second?: string]',
    '[...number[]]', '[head: number, ...tail: string[]]', '[...number[], string]',
    '[first: number, string]', '[number, second: string]', '[x?: number, ...y: string[]]',
    'Result & { value: number }', '& Result & Other', 'Result | Other & { value: number }',
    '(Result & Other)[]', '((x: number) => string) | undefined',
    '{\nvalue: number\nname?: string\n}', '-42', '-42n',
    'Array<{ value: number } & Other>', '[(x: number) => string, { value: number }]',
    '(x: number) => (y: string) => [number, string]',
    '{ f(this: Result, x?: number): this; value: this }'
  ];
  const contexts = [
    t => `const value: ${t} = 42; value;`,
    t => `function f(value?: ${t}): ${t} { return value ?? 42; } f();`,
    t => `const f = (value?: ${t}): ${t} => value ?? 42; f();`,
    t => `const f = (value): ${t} => value; f(42);`,
    t => `const f = async (value?: ${t}): Promise<${t}> => value ?? 42; f();`,
    t => `const o = { f(value?: ${t}): ${t} { return value ?? 42; } }; o.f();`,
    t => `class C { f(value?: ${t}): ${t} { return value ?? 42; } } new C().f();`,
    t => `type Alias<T = number> = ${t}; const value: Alias = 42; value;`,
    t => `interface Shape<T = number> extends Base<T>, Other { value: ${t}; } const value: Shape = 42; value;`,
    t => `function f() { type Alias = ${t}; interface Shape { value: Alias } return 42; } f();`
  ];
  const cases = types.flatMap((type, i) => contexts.map((context, j) => ({ name: `rich-${i}-${j}`, source: context(type) })));
  const focused = [
    'const f = (value?) => value ?? 42; f();',
    'const f = async (value?) => value ?? 42; f();',
    'function f(value?) { return value ?? 42; } f();',
    'function f(value?: number, other: number = 42) { return other; } f();',
    'class C { constructor(x?: number) { this.x = x ?? 42; } } new C().x;',
    'const f = (x? /* 😀 */ : number, y?: number) => (x ?? 40) + (y ?? 2); f();',
    'let type = 40; type\n+2;',
    'let type = 40, value = 0; type\nvalue = 42; value;',
    'const type = { value: 42 }; type.value;',
    'type: for (;;) { break type; } 42;',
    'type Value = number\nconst value: Value = 42; value;',
    'interface Shape {}\n/ok/.test("ok") ? 42 : 0;',
    'interface Shape {}; 42;',
    'type T = number; "use strict"; 42;',
    'interface Shape {} "use asm"; 42;',
    'function f() { type T = number; "use strict"; return 42; } f();',
    'type Shape = { value: number }; const Shape = { value: 42 }; Shape.value;',
    'let value = 0; { type T = number; interface I { x: T } value = 42; } value;',
    'let value = 0; switch (1) { case 1: type T = number; interface I { x: T } value = 42; break; } value;',
    'class C { static { type T = number; interface I { x: T } this.value = 42; } } C.value;',
    'interface Shape extends A<Array<B<C>>>, D { value: number } 42;',
    'type A = { value: number }; type B = A & { next?: B }; 42;',
    'function f(x?: {v: number}) { return x ? x.v : 42; } f();'
  ];
  focused.forEach((source, i) => cases.push({ name: `rich-focused-${i}`, source }));
  const modules = [
    'import type Value from "missing"; export const value: Value = 42;',
    'import type from from "missing"; export const value: from = 42;',
    'export type { "quoted-name" as Value } from "missing";',
    'import { type default as Value } from "values";',
    'export { type default as Value } from "values";',
    'import type * as Types from "missing"; export const value: Types.Value = 42;',
    'import type { Value, Other as Alias } from "missing"; export const value: Value = 42;',
    'import type { "not-an-id" as Value, default as Other } from "missing"; export const value: Value = 42;',
    'import type { Value } from "missing";',
    'import type Value from "missing"; const value: Value = 42;',
    'export type Value = { value: number };',
    'export interface Shape<T> extends Base<T> { value: T }',
    'export default interface Shape { value: number }',
    'type Value = number; export type { Value };',
    'type Value = number; export type { Value as Other }; export const value = 42;',
    'export type { Value, Other as Alias } from "missing";',
    'export type * from "missing";',
    'export type * as Types from "missing";',
    'import { type Value, value as result } from "values"; export { result };',
    'import { type Value } from "values";',
    'import { type Value, } from "values"; export const value = 42;',
    'import value, { type Value } from "values"; export { value };',
    'import { type as value } from "values"; export { value };',
    'import { type as } from "values";',
    'import { type as as } from "values";',
    'import { type as as Value } from "values";',
    'import type from "values"; export { type };',
    'import type, { value } from "values"; export { type, value };',
    'type Value = number; const value = 42; export { type Value, value };',
    'type Value = number; export { type Value };',
    'export { type Value, value } from "values";',
    'export { type Value } from "values";',
    'export { type as value } from "values";',
    'export { type as } from "values";',
    'export { type as as } from "values";',
    'export { type as as Value } from "values";',
    'import type Value from "missing"; "use asm"; const value = 42;',
    'export interface Shape {} export const Shape = 42;',
    'type Value = number; export type { Value }; export default 42;'
  ];
  modules.forEach((source, i) => cases.push({ name: `rich-module-${i}`, source, module: true }));

  // Delete or insert structural tokens in independently valid source. Keep only
  // mutations rejected by Babel; successful mutations need not be invalid TS.
  let seed = 0x79212419;
  const next = n => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) % n; };
  function composite(depth) {
    if (!depth) return ['number', 'string', 'T', 'null', '42'][next(5)];
    const child = () => composite(depth - 1);
    switch (next(8)) {
      case 0: return `{ readonly value?: ${child()}; next: ${child()} }`;
      case 1: return `((x?: ${child()}) => ${child()})`;
      case 2: return `[first: ${child()}, second?: ${child()}]`;
      case 3: return `(${child()} & ${child()})`;
      case 4: return `(${child()} | ${child()})`;
      case 5: return `Result<${child()}, ${child()},>`;
      case 6: return `(${child()})[]`;
      default: return `{ [key: string]: ${child()}; f(x: ${child()}): ${child()} }`;
    }
  }
  for (let i = 0; i < 600; i++) {
    let source = contexts[i % contexts.length](composite(1 + next(4)));
    if (i % 3 === 0) source = source.replaceAll(': ', ': /* 😀 */\r\n ');
    cases.push({ name: `rich-composite-${i}`, source });
  }
  const seeds = [
    'type T = { readonly x ? : number ; f ( x : number ) : string ; } ;',
    'interface I < T > extends Base < T > { [ key : string ] : T ; }',
    'type T = [ first : number , second ? : string , ... rest : number [ ] ] ;',
    'type T = < U extends Result < number > > ( x : U ) => U ;',
    'const f = ( x ? : { v : number } ) => x ;',
    'function f ( x ? : [ number , string ] ) { return x ; }'
  ];
  const invalid = new Set();
  const tokens = ['{', '}', '(', ')', '[', ']', '<', '>', '?', ':', ';', ',', '&', '|', '=>', '=', '...', 'number'];
  for (let i = 0; i < 5000; i++) {
    const sourceTokens = seeds[next(seeds.length)].split(' ');
    const at = next(sourceTokens.length);
    if (i % 2) sourceTokens.splice(at, 1);
    else sourceTokens.splice(at, 0, tokens[next(tokens.length)]);
    const source = sourceTokens.join(' ');
    try { parse(source, { sourceType: 'script', plugins: ['typescript'] }); }
    catch { invalid.add(source); }
  }
  return { cases, invalid: [...invalid].sort().map((source, i) => ({ name: `rich-invalid-${i}`, source })) };
}
