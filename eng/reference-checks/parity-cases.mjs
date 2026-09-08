// Development-only syntax/emission oracles for assertions and the remaining
// original experiment forms. No JavaScript compiler is used by the library.
export function parityCases(parse) {
  const cases = [];
  const expressions = [
    'value as number', 'value as number + 2', 'value + 2 as number',
    'value as number * 2', 'value * 2 as number', '(value as number) + 2',
    'value as number ** 2', '-value as number', '!(value as number)',
    'value as number > 2', 'value as number >= 2', 'value as number < 42',
    'value as number <= 42', 'value as number >> 1', 'value as number >>> 1',
    'value as number << 1', 'value as number + 4 >> 1',
    'value === 40 as number', 'value as number === 40',
    'value as number ? 42 : 0', 'false ? 0 : value as number + 2',
    '(value as number, 42)', 'value as number as unknown as number',
    'value satisfies number', 'value satisfies number + 2',
    'value + 2 satisfies number', 'value satisfies number >= 2',
    'value as const', 'value as const >> 1',
    '(value as number) && 42', '(value as number) || 42',
    'null as unknown ?? 42', '(null ?? value) as number + 2',
    'value as number | string', 'value as number & Other',
    'value as (number | string)[]', 'value as {value: number}',
    'value as [number, string?]', 'value as ((x: number) => number)',
    'value as Array<Array<number>> >> 1', 'value as Array<number> >= 2',
    'value as Array<Array<number>> >>> 1', 'value as keyof Shape',
    'value as Shape["value"]', 'value as `value${number}`',
    'value as { [P in keyof Shape]: Shape[P] }',
    '-value as number ** 2', '(value + 2) as number ** 2', 'value ** 2 as number ** 3',
    'value as number ** -2', 'value as number ** 2 ** 3', 'value as number ** 2 as number ** 3'
  ];
  const contexts = [
    e => `const value = 40; ${e};`,
    e => `const value = 40; const result = ${e}; result;`,
    e => `function f(value: number) { return ${e}; } f(40);`,
    e => `const f = (value: number) => ${e}; f(40);`,
    e => `const value = 40; function f(x) { return x; } f(${e});`,
    e => `const value = 40; const result = [${e}]; result[0];`
  ];
  expressions.forEach((e,i) => contexts.forEach((context,j) => cases.push({name:`assert-${i}-${j}`,source:context(e)})));
  const focused = [
    'const value = 40; value! + 2;',
    'const value = 40; value!!!!!!!!!! + 2;',
    'const value = {x: 42}; value!.x;',
    'const value = {x: {y: 42}}; value!.x!.y;',
    'const value = {x: 42}; value!["x"];',
    'const value = [42]; value![0]!;',
    'const f = x => x + 1; f!(41);',
    'const o = {value: 42, f() { return this.value; }}; o.f!();',
    'const f = (strings) => strings[0]; f!`42`;',
    'class C { constructor() { this.value = 42; } } new C!().value;',
    'class C { constructor() { this.value = 42; } } new C()!.value;',
    'let value = 41; value!++; value;',
    'let value = 41; ++value!; value;',
    'let value = 0; (value as number) = 42; value;',
    'let value = 0; value! = 42; value;',
    'let value = 0; ({x: value as number} = {x: 42}); value;',
    'let value = 0; [value!] = [42]; value;',
    'let value = 0; for (value! of [20, 42]) {} value;',
    'let value = 0; for (value as number of [20, 42]) {} value;',
    'let value = 42; const f = (x = value as number) => x; f();',
    'let value = 42; const f = async (x = value!) => x; f();',
    'const o = {f(x = 42 as number) {return x;}}; o.f();',
    'const value = 42; async function f() {return (await value) as number;} f();',
    'function* f() {yield 42 as number;} f().next().value;',
    'const value = 40; value as /* 😀 */\r\nnumber + 2;',
    'const value = 40; value /* 😀 */ ! + 2;',
    'const value = 42; value\n!false;',
    'const value = 42; value as number\n[0];',
    'const value = 42; value as number\n>> 1;',
    'const as = 40, satisfies = 2; as + satisfies;',
    'const value = {as: 40, satisfies: 2}; value.as + value.satisfies;',
    'const as = x => x; const satisfies = x => x; satisfies(as(42));',
    'const value = 40; const x = /a/.test("a") ? value as number + 2 : 0; x;',
    'const value = 84; value! / 2;',
    'const value = 84; value as number / 2;',
    'const value = 42; `${value as number}`;',
    'function merge<T, U>(a: T, b: U): T & U { return Object.assign(a, b); } merge({x: 40}, {y: 2}).x + 2;',
    'function f<T extends {value: number} = {value: number}>(x: T): number {return x.value;} f({value: 42});',
    'const f = function<T>(x: T): T { return x; }; f(42);',
    'const f = function named<T, U = T,>(x: T): T { return x; }; f(42);',
    'async function f<T>(x: T): Promise<T> {return x;} f(42);',
    'function* f<T>(x: T) { yield x; } f(42).next().value;',
    'function outer() { function f<T>(x: T): T {return x;} return f(42); } outer();',
    'function f<T = number>(x?: T) {return x ?? 42;} f();'
  ];
  focused.forEach((source,i)=>cases.push({name:`parity-focused-${i}`,source}));
  for(const optional of ['null', '{value: 42, f() {return this.value;}}']) {
    for(const expression of ['o?.value!', 'o?.value!.x', 'o?.f!()', 'o!?.value', '(o?.value)!', '(o?.f)!?.()', 'o?.f!().x'])
      cases.push({name:`nonnull-chain-${cases.length}`,source:`const o = ${optional}; ${expression};`});
  }
  const types = [
    '`plain`', '`value-${string}`', '`prefix-${number}-suffix`', '`a${string | number}b${boolean}c`',
    '`a${`b${number}`}c`', '`line\n${number}\r\n😀`', '`escaped\\`${number}`', '`escaped\\n${number}`',
    '`escaped\\${literal}`', '`value${{x: number}}`',
    'keyof Shape', '(keyof Shape)[]', 'keyof Shape[]', 'keyof keyof Shape',
    'Shape["value"]', 'Shape[keyof Shape]', 'Shape[keyof Shape][]', 'Shape["value"][number]',
    '{ [P in keyof Shape]: Shape[P] }', '{ [P in "x" | "y"]?: number }',
    '{ readonly [P in keyof Shape]: Shape[P] }', '{ +readonly [P in keyof Shape]-?: Shape[P] }',
    '{ -readonly [P in keyof Shape]+?: Shape[P] }', '{ [P in keyof Shape] }',
    '{ [P in keyof Shape as `get${P}`]: Shape[P] }', '{ [P in keyof Shape]: { [Q in keyof Shape[P]]: Shape[P][Q] } }',
    '{ [P in "x" | "y"]: number; }', 'Array<{[P in keyof Shape]: Shape[P]}>',
    '{[P in keyof Shape]: (x: Shape[P]) => Shape[P]}', '{[P in keyof Shape]: [`value${P}`, Shape[P]]}'
  ];
  const typeContexts = [
    t => `type T = ${t}; const value: T = 42; value;`,
    t => `const value: ${t} = 42; value;`,
    t => `function f(x: ${t}): ${t} {return x;} f(42);`,
    t => `const f = (x): ${t} => x; f(42);`,
    t => `const f = async (x: ${t}): Promise<${t}> => x; f(42);`,
    t => `interface I<T = ${t}> { value: T } const value: I = 42; value;`,
    t => `function f<T extends ${t}>(x: T): T {return x;} f(42);`
  ];
  types.forEach((t,i)=>typeContexts.forEach((context,j)=>cases.push({name:`parity-type-${i}-${j}`,source:context(t)})));
  [
    'export function f<T>(x: T): T { return x; }',
    'export default function<T>(x: T): T { return x; }',
    'export default async function<T>(x: T): Promise<T> { return x; }',
    'export type M<T> = { [P in keyof T]: T[P] }; export const value = 42 as number;',
    'export type T = `value${number}`; export const value = 42!;'
  ].forEach((source,i)=>cases.push({name:`parity-module-${i}`,source,module:true}));

  let seed = 0x57a52124;
  const next = n => {seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed>>>0)%n;};
  const originals = [
    'const value = 42 as { x : number } ;',
    'const value = 42 satisfies [ number , string ? ] ;',
    'function f < T extends A < number > = B > ( x : T ) : T { return x ! ; }',
    'type T = { + readonly [ P in keyof Shape as `name${P}` ] - ? : Shape [ P ] ; } ;',
    'type T = `value${number}` ;'
  ];
  const tokens = ['<','>','[',']','{','}','(',')','?',':',';','as','satisfies','readonly','keyof','in','=','=>','!',',','`','${'];
  const invalid = new Set();
  for(let i=0;i<6000;i++) {
    const parts=originals[next(originals.length)].split(' '); const at=next(parts.length);
    if(i%2) parts.splice(at,1); else parts.splice(at,0,tokens[next(tokens.length)]);
    const source=parts.join(' ');
    try {parse(source,{sourceType:'script',plugins:['typescript']});} catch {invalid.add(source);}
  }
  return {cases,invalid:[...invalid].sort().map((source,i)=>({name:`parity-invalid-${i}`,source}))};
}
