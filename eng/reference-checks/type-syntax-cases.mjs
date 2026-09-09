// Erasure-only type syntax. Both reference parsers validate the positive corpus;
// mutation negatives are retained only when Babel rejects the exact source.
export function typeSyntaxCases(parse) {
  const types = [
    'T extends U ? number : string',
    'T extends U ? X extends Y ? number : string : boolean',
    'T extends U ? number : X extends Y ? string : boolean',
    'T extends (U extends V ? X : Y) ? number : string',
    'T extends infer U ? U : never',
    'T extends infer U extends string ? U : never',
    'T extends Array<infer U> ? U : never',
    'T extends [infer Head, ...infer Tail] ? Head : never',
    'T extends `${infer Head}-${infer Tail extends number}` ? Head : never',
    'T extends (x: infer U) => infer R ? R : never',
    'T extends () => U ? number : string',
    'T extends new () => infer R ? R : never',
    'T extends {x: infer U extends (A | B)} ? U : never',
    '(T extends U ? X : Y)[]',
    '{[K in keyof T as K extends string ? K : never]: T[K] extends infer U ? U : never}',
    'import("missing-types")',
    'import("missing-types").Shape',
    'import("missing-types").Domain.Shape<Array<number>, string,>',
    'import("missing-types").default',
    'typeof import("missing-types")',
    'typeof import("missing-types").factory<Array<number>>',
    'import("missing-types").Shape["value"]',
    'T extends import("missing-types").Shape<infer U> ? U : never',
    'infer U extends string',
    'infer U extends string ? number : boolean'
  ];
  const contexts = [
    t => `type Result = ${t}; 42;`,
    t => `interface I {value: ${t}} 42;`,
    t => `let x: ${t} = 42; x;`,
    t => `function f(x: ${t}): ${t} {return x;} f(42);`,
    t => `const f = (x: ${t}): ${t} => x; f(42);`,
    t => `const f = (x): ${t} => x; f(42);`,
    t => `function f<const X extends ${t} = ${t}>(x: X) {return x;} f(42);`,
    t => `const f = async<const X extends ${t}>(x: X) => x; f(42);`,
    t => `const f = <const X extends ${t}>(x: X) => x; f(42);`,
    t => `class C<const X extends ${t}> {value: ${t} = 42;} new C().value;`,
    t => `declare const host: ${t}; 42;`,
    t => `function f(x: ${t}): ${t}; function f(x) {return x;} f(42);`,
    t => `const f = x => x; f<${t}>(42);`,
    t => `const f = x => x; f?.<${t}>(42);`,
    t => `class C {value=42;} new C<${t}>().value;`,
    t => `const tag = () => 42; tag<${t}>\`value\`;`,
    t => `42 as ${t} + 1;`,
    t => `42 satisfies ${t} >> 1;`,
    t => `type Callback = <const X>(x: ${t}) => ${t}; 42;`,
    t => `const o = {f<const X>(x: ${t}): ${t} {return x;}}; o.f(42);`
  ];
  const cases = types.flatMap((t, i) => contexts.map((context, j) => ({
    name: `type-syntax-${i}-${j}`, source: context(t)
  })));
  const add = (name, sources, module = false) => sources.forEach((source, i) =>
    cases.push({name: `type-syntax-${name}-${i}`, source, ...(module ? {module:true} : {})}));
  add('modifiers', [
    'type Consumer<in T> = (x:T) => void; 42;',
    'type Producer<out T> = () => T; 42;',
    'type Invariant<in out T> = {value:T}; 42;',
    'interface I<in out T extends Shape = Shape> {value:T} 42;',
    'class C<in out const T> {value=42;} new C().value;',
    'class C<const in out T> {value=42;} new C().value;',
    'class C<in const out T> {value=42;} new C().value;',
    'const C = class<const T> {f<const U>(x:U) {return x;}}; new C().f(42);',
    'function* f<const T>(x:T) {yield x;} f(42).next().value;',
    'async function f<const T>(x:T) {return x;} f(42);',
    'const f = function<const T>(x:T) {return x;}; f(42);',
    'const f = async<const T,>(x:T) => x; f(42);',
    'type T<out> = out; interface I<out> {} class C<out> {} 42;',
    'type T<out = number> = out; const f = <const out>(x:out) => x; f(42);',
    'type T = {<const X>(x:X):X; new<const X>(x:X):X}; 42;',
    'type T = T extends infer U extends Array<infer V> ? V : never; 42;',
    'type T = T extends (infer U extends X ? Y : Z) ? number : string; 42;',
    'type T = (infer U extends X) extends Y ? number : string; 42;',
    'type T = T extends U ? () => X : () => Y; 42;',
    'type T = T extends (() => U extends X ? Y : Z) ? number : string; 42;',
    'type T = T extends infer U extends infer V extends string ? U : never; 42;',
    'type T = T /* same line */ extends U ? X : Y; 42;',
    'type T = infer U\nextends string; 42;'
  ]);
  add('modules', [
    'export type T = import("missing").Shape; export const answer = 42;',
    'export interface I<out T> {value:T} export function f<const T>(x:T) {return x;}',
    'export type T<X> = X extends infer U ? U : never;',
    'import type {Shape} from "missing"; export type T = typeof import("missing");',
    'export const f = async<const T>(x:T) => x;'
  ], true);
  // Seeded composition probes exercise parentheses, generic closers, unions,
  // mapped members, function precedence and conditional associativity together.
  let seed = 0x19c02124;
  const next = n => {seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%n;};
  for (let i = 0; i < 400; i++) {
    let type = types[next(types.length)];
    for (let depth = 0; depth < next(4) + 1; depth++) {
      const other = types[next(types.length)];
      type = [() => `Array<${type}>`, () => `(${type}) | (${other})`,
        () => `(${type}) extends (${other}) ? ${type} : never`,
        () => `{x: ${type}; f<const X>(x: ${other}): ${type}}`,
        () => `T extends {x: ${type}} ? ${other} : ${type}`][next(5)]();
    }
    cases.push({name:`type-syntax-composed-${i}`,source:contexts[next(contexts.length)](type)});
  }
  const originals = [
    'type T = X extends Y ? X : Y ;',
    'type T = X extends infer U extends string ? U : never ;',
    'type T = X extends [ infer U , ... infer V ] ? U : never ;',
    'type T = X extends ( x : infer U ) => infer R ? R : never ;',
    'type T = import ( "missing" ) . Shape < number > ;',
    'type T = typeof import ( "missing" ) . factory < number > ;',
    'function f < const T extends Shape = Shape > ( x : T ) { return x ; }',
    'const f = async < const T > ( x : T ) => x ;',
    'type T < in out X > = { x : X } ;',
    'class C < const in out T > { value : T ; }'
  ];
  const tokens = ['extends','infer','const','in','out','import','typeof','number','T','"missing"',
    '<','>','[',']','{','}','(',')','?',':',';','=','=>','!',',','async','...','\n'];
  const invalid = new Set();
  for (let i = 0; i < 14000; i++) {
    const parts = originals[next(originals.length)].split(' ');
    parts.splice(next(parts.length), next(2), tokens[next(tokens.length)]);
    const source = parts.join(' ');
    try {parse(source,{plugins:['typescript']});} catch {invalid.add(source);}
  }
  return {cases, invalid:[...invalid].sort().map((source,i)=>({name:`type-syntax-invalid-${i}`,source}))};
}
