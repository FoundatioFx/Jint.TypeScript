// Development-only syntax and emission oracles. No compiler ships at runtime.
export function signatureCases(parse) {
  const cases = [];
  const add = (group, sources, module = false) => sources.forEach((source, i) =>
    cases.push({ name: `signatures-${group}-${i}`, source, ...(module ? { module } : {}) }));
  const types = ['readonly number[]', 'readonly [number, string?]', 'readonly (number[])',
    'readonly (number | string)[]', 'readonly [head: number, ...tail: string[]]',
    '(readonly number[])[number]', 'keyof readonly [number]', 'readonly T[keyof T][]',
    'ReadonlyArray<readonly number[]>', 'unique symbol', 'typeof value', 'typeof obj.value',
    'typeof obj.default', 'typeof this', 'typeof identity<number>', 'typeof identity<Array<number>,>',
    'typeof obj.value[]', '(typeof value)[keyof typeof value]', 'typeof value | undefined',
    'typeof identity<(<T>(x:T)=>T)>', '{[P in keyof typeof obj]: (typeof obj)[P]}',
    '(x: unknown) => x is number', '(x: unknown) => asserts x', '(x: unknown) => asserts x is number',
    '{check(x: unknown): x is number; assert(x: unknown): asserts x is number}',
    '(this: typeof obj, x: unknown) => asserts x', 'asserts', 'asserts.Value'];
  const contexts = [
    t => `type A = ${t}; 42;`,
    t => `let x: ${t} = 42; x;`,
    t => `function f(x: ${t}): ${t} {return x;} f(42);`,
    t => `const f = (x: ${t}): ${t} => x; f(42);`,
    t => `const f = (x): ${t} => x; f(42);`,
    t => `class C {value: ${t} = 42;} new C().value;`,
    t => `const f = x=>x; f<${t}>(42);`,
    t => `interface I<T = ${t}> {value: ${t}} 42;`
  ];
  types.forEach((t, i) => add(`type-${i}`, contexts.map(c => c(t))));
  const returns = ['x is number', 'x is readonly number[]', 'x is typeof value',
    'x is T & {value: number}', 'asserts x', 'asserts x is number',
    'asserts x is readonly [number]', 'this is Shape', 'asserts this', 'asserts this is Shape',
    'x /* comment */ is /* comment */ number', 'asserts /* comment */ x is number'];
  returns.forEach((r, i) => add(`predicate-${i}`, [
    `function f(x: unknown): ${r} {return true;} f(42);`,
    `const f = function(x: unknown): ${r} {return true;}; f(42);`,
    `const f = (x: unknown): ${r} => true; f(42);`,
    `const f = (x): ${r} => true; f(42);`,
    `const o = {f(x: unknown): ${r} {return true;}}; o.f(42);`,
    `class C {f(x: unknown): ${r} {return true;}} new C().f(42);`,
    `type Predicate = (x: unknown) => ${r}; 42;`,
    `interface I {f(x: unknown): ${r}; (x: unknown): ${r}} 42;`
  ]));
  ['Context', 'typeof obj', '{value: number}', 'readonly number[]', 'This & Context',
    'Object<string, readonly number[]>', '(x: unknown) => x is number'].forEach((t, i) => add(`this-${i}`, [
    `function f(this: ${t}, x: number) {return this.value+x;} f.call({value:40},2);`,
    `const f = function(this: ${t}, x=2) {return this.value+x;}; f.call({value:40});`,
    `async function f(this: ${t}, x: number) {return this.value+x;} f.call({value:40},2);`,
    `function* f(this: ${t}, x: number) {yield this.value+x;} f.call({value:40},2).next().value;`,
    `const o = {value:40, f(this: ${t}, x: number) {return this.value+x;}}; o.f(2);`,
    `class C {value=40; f(this: ${t}, x: number) {return this.value+x;}} new C().f(2);`,
    `function f<T>(this: ${t}, ...x: T[]) {return this.value+x[0];} f.call({value:40},2);`,
    `function f(this: ${t},) {return this.value;} f.call({value:42});`
  ]));
  const params = ['x: number', 'x: string', 'x?: number', '...x: number[]',
    'this: Context, x: number', '{x}: {x: number}', '[x]: [number]', '{x=42}: {x?: number}',
    'x: (v: unknown) => v is number', 'x: readonly (typeof value)[]'];
  params.forEach((p,i) => add(`overload-${i}`, [
    `function f(${p}): number; function f(x: unknown): number {return 42;} f(42);`,
    `function f<T>(${p}): T; function f<T>(x: unknown): T {return 42;} f<number>(42);`,
    `function outer() {function f(${p}): number; function f(x) {return 42;} return f(42);} outer();`,
    `{function f(${p}): number; function f(x) {return 42;} f(42);}`,
    `class C {f(${p}): number; f(x) {return 42;}} new C().f(42);`,
    `class C {static f(${p}): number; static f(x) {return 42;}} C.f(42);`,
    `function f(${p}): number; typeof f;`,
    `declare function f(${p}): number; typeof f;`
  ]));
  types.slice(0, 21).forEach((t,i) => add(`ambient-${i}`, ['var','let','const'].map(k =>
    `declare ${k} value: ${t}, other: ${t}; typeof value + typeof other;`)));
  add('focused', [
    'function f(this: Context, x: unknown): x is number; function f(this: Context, x: unknown): asserts x; function f(this: Context, x: unknown) {return this.value+x;} f.call({value:40},2);',
    'function f(this: Context, x: number) {"use strict"; return this.value+x;} f.call({value:40},2);',
    'function f(this: Context, x: number) {return [f.length, arguments.length, arguments[0]];} JSON.stringify(f.call(null,42));',
    'function f(this: Context) {return [f.length, arguments.length];} JSON.stringify(f.call(null,42));',
    'function f(this: Context, x=42) {return [f.length, arguments.length, x];} JSON.stringify(f.call(null));',
    'class C {constructor(x:number); constructor(x:string); constructor(x) {this.value=42;}} new C(0).value;',
    'class C {"value"(): number; "value"() {return 42;}} new C().value();',
    'class C {42(x: number): number; 42(x) {return x;}} new C()[42](42);',
    'class C {#f(x: number): number; #f(x) {return x;} run() {return this.#f(42);}} new C().run();',
    'let calls=0; function key() {calls++; return "f";} class C {[key()](x: number): number; [key()](x) {return x;}} new C().f(42)+calls;',
    'function f(x:number)\nfunction f(x) {return x;} f(42);',
    'class C {f(x:number):number\nf(x) {return x;}} new C().f(42);',
    'async function f(x:number):Promise<number>; async function f(x) {return x;} f(42);',
    'function* f(x:number):Iterable<number>; function* f(x) {yield x;} f(42).next().value;',
    'declare function* f(): Iterable<number>; typeof f;',
    'class C {async f(x:number):Promise<number>; async f(x) {return x;}} new C().f(42);',
    'class C {*f(x:number):Iterable<number>; *f(x) {yield x;}} new C().f(42).next().value;',
    'declare const a=42,b=-42,c=42n,d=-42n,e="answer",f=true,g=false; typeof a+typeof b+typeof c+typeof d+typeof e+typeof f+typeof g;',
    'declare var x; declare let y; declare const z; typeof x+typeof y+typeof z;',
    'declare interface Shape {x: number} declare type T=readonly number[]; 42;',
    'function f(this: Context) {return /ok/.test("ok") ? 42 : 0;} f();',
    'function f(x: unknown): asserts /*same line*/ x {return 42;} f(0);',
    'const f = true ? (x): x is number => true : (x): asserts x => true; f(42);',
    'const asserts=42, is=2, readonly=3, unique=4, declare=5; asserts+is+readonly+unique+declare;'
  ]);
  add('module', [
    'export function f(x:number):number; export function f(x:string):string; export function f(x) {return x;}',
    'export default function f(x:number):number; export default function f(x) {return x;}',
    'export default function(x:number):number; export default function(x) {return x;}',
    'export function f(x:number):number;',
    'export default function f(x:number):number;',
    'export declare function f(this: Context, x: unknown): asserts x is number;',
    'export declare const host: typeof value;',
    'export declare let first: number, second: readonly number[];',
    'export declare interface Shape {value: number}',
    'export declare type T = readonly [number];',
    'declare function host(x: number): number; export const value: number = 42;',
    'export class C {constructor(x:number); constructor(x) {} f(this:C, x:unknown): x is number; f(x) {return true;}}',
    'import type {Context} from "types"; export function f(this:Context, x:number):number; export function f(this:Context, x:number) {return x;}'
  ], true);
  const originals = [
    'type T = readonly ( typeof value ) [ ] ;',
    'type T = readonly [ first : number , ... rest : string [ ] ] ;',
    'type T = unique symbol ;',
    'type T = typeof identity < Array < number > > ;',
    'function f ( this : Context , x : unknown ) : x is number { return true ; }',
    'function f ( this : Context , x : unknown ) : asserts x is number { return ; }',
    'const f = ( x : unknown ) : x is number => true ;',
    'function f < T > ( x : T ) : T ; function f ( x ) { return x ; }',
    'class C { constructor ( x : number ) ; constructor ( x ) { } f ( x : number ) : number ; f ( x ) { return x ; } }',
    'declare const x : readonly number [ ] ;',
    'declare function f ( x : unknown ) : asserts x is number ;'
  ];
  const tokens = ['this','is','asserts','typeof','readonly','unique','symbol','declare','function','const',
    '<','>','[',']','{','}','(',')','?',':',';','=','=>','!',',','async','static','number','...','\n'];
  let seed=0x7212124;
  const next=n=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%n;};
  const invalid=new Set();
  for(let i=0;i<14000;i++) {
    const parts=originals[next(originals.length)].split(' ');
    parts.splice(next(parts.length),next(2),tokens[next(tokens.length)]);
    const source=parts.join(' ');
    try {parse(source,{plugins:['typescript']});} catch {invalid.add(source);}
  }
  // Babel 8 rejects async arrow predicates that tsc emits successfully. Keep
  // this exact discovered case as an explicit tsc-only oracle, not a negative.
  const asyncPredicate = 'const f = async ( x : unknown ) : x is number => true ;';
  invalid.delete(asyncPredicate);
  cases.push({name:'signatures-babel-async-predicate', source:asyncPredicate + ' f(42);',
    babelUnsupported:'Babel 8 async arrow return predicate parsing'});
  return {cases, invalid:[...invalid].sort().map((source,i)=>({name:`signatures-invalid-${i}`,source}))};
}
