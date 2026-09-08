// Development-only differential checks. The shipped parser and AST are native C#.
export function genericsCases(parse) {
  const cases = [];
  const types = ['number', 'string', 'unknown', 'never', 'void', 'null', 'true', '42', '-42', '42n',
    'T', 'Domain.T', 'Array<Array<number>>', 'T & U', 'T | U', '{x: Array<T>}', '[number, string?]',
    '() => number', '<T>() => T', '<T extends U>(x: T) => T', '(T | U)[]', 'T[keyof T]',
    '{[P in keyof T]: T[P]}', '`value${number}`', '`a${`b${number}`}c`',
    '{f<T>(): T}', 'new<T>(x:T) => T', 'number, string', 'Array<T>,', 'T /* > ( */'];
  const contexts = [
    t => `function f<T>(x: T) {return x;} f<${t}>(42);`,
    t => `const f = x => x; f /* comment */ <${t}> (42);`,
    t => `const o = {n: 42, f<T>() {return this.n;}}; o.f<${t}>();`,
    t => `const f = x => x; const o = null; o?.f<${t}>(f<${t}>(42));`,
    t => `const f = x => x; f?.<${t}>(42);`,
    t => `const f = null; let n = 42; f?.<${t}>(++n); n;`,
    t => `class C<T> {value: T; constructor(x: T) {this.value=x;}} new C<${t}>(42).value;`,
    t => `class C<T> {value = 42;} (new C<${t}>).value;`,
    t => `const f = x => x; const g = f<${t}>; g(42);`,
    t => `const tag = (s, x) => x; tag<${t}>` + '`value${42}`;',
    t => `const f = x => x; 40 < f<${t}>(42);`,
    t => `const f = x => x; const async = f; async<${t}>(42);`
  ];
  types.forEach((t,i)=>contexts.forEach((context,j)=>cases.push({name:`generics-type-${i}-${j}`,source:context(t)})));
  const heads = ['T', 'T,', 'T, U', 'T extends number', 'T = number', 'T extends {value: number} = {value: number}',
    'T extends Array<Array<number>>', 'T extends (x: number) => number, U = T', 'T extends {[P in keyof U]: U[P]}'];
  const headContexts = [
    h => `const f = <${h}>(x: T): T => x; f<number>(42);`,
    h => `const f = async <${h}>(x: T): Promise<T> => x; f<number>(42);`,
    h => `const f = <${h}>(x = 42) => x; f();`,
    h => `const f = async <${h}>(x = /ok/.test('ok') ? 42 : 0) => x; f();`,
    h => `const o = {f<${h}>(x: T): T {return x;}}; o.f<number>(42);`,
    h => `const o = {async f<${h}>(x: T): Promise<T> {return x;}}; o.f<number>(42);`,
    h => `const o = {*f<${h}>(x: T) {yield x;}}; o.f<number>(42).next().value;`,
    h => `class C<${h}> {value: T = 42;} new C<number>().value;`,
    h => `class C {f<${h}>(x: T): T {return x;}} new C().f<number>(42);`,
    h => `class C {static async f<${h}>(x: T): Promise<T> {return x;}} C.f<number>(42);`
  ];
  heads.forEach((h,i)=>headContexts.forEach((context,j)=>cases.push({name:`generics-head-${i}-${j}`,source:context(h)})));
  const fields = ['value: number = 42', 'value?: number = 42', 'value!: number', 'value?: number', 'value: number',
    'readonly value: number = 42', 'public readonly value: number = 42', 'protected value: number = 42',
    'private value: number = 42', 'public value?: number', 'override value: number = 42',
    'static readonly value: number = 42', 'public static readonly value: number = 42',
    '#value: number = 42', '["value"]: number = 42', 'readonly: number = 42', 'public: number = 42',
    'override: number = 42', 'async: number = 42', 'static: number = 42', 'get: number = 42', 'set: number = 42',
    'readonly #value: number = 42', 'readonly static: number = 42', 'readonly override: number = 42',
    'readonly public: number = 42', '#value?: number', '#value!: number'];
  fields.forEach((f,i)=>cases.push({name:`generics-field-${i}`,source:`class Base {} class C<T> extends Base {${f};} JSON.stringify([Object.keys(new C<number>()), Object.keys(C)]);`}));
  const focused = [
    'class Base<T> {value = 42;} class C<T> extends Base<T> {} new C<number>().value;',
    'class Base<T> {value = 42;} class C<T> extends Base<T> implements A<T>, B {} new C<number>().value;',
    'const C = class<T> {value: T = 42;}; new C<number>().value;',
    'class C<T> implements A<T>, Domain.B<Array<T>> {value = 42;} new C().value;',
    'class C {public constructor(x: number) {this.value=x;} private value: number; protected f<T>(): number {return this.value;} run() {return this.f<number>();}} new C(42).run();',
    'class C {f?<T>(x: T): T {return x;}} new C().f?.<number>(42);',
    'const o = {async<T>(x:T):T {return x;}, get<T>(x:T):T {return x;}}; o.async<number>(40) + o.get<number>(2);',
    'class C {static<T>(x:T):T {return x;} async<T>(x:T):T {return x;}} new C().static<number>(40) + new C().async<number>(2);',
    'const f = <T>({x}: {x:T} = {x:42}): T => x; f<number>();',
    'const f = async <T>(...x: T[]): Promise<T> => x[0]; f<number>(42);',
    'const f = <T>(x = <U>(y:U) => y) => x<number>(42); f();',
    'const f = <T>(x = async <U>(y:U) => y) => x<number>(42); f();',
    'const f = x=>x; (f<number>)?.(42);',
    'const f = x=>x; f<number>?.(42);',
    'function f() {const local = 42; return eval<string>("local");} f();',
    'const o = {value:42, f(){return this.value;}}; (o.f<number>)();',
    'const f = x=>x; f<number>\n(42);',
    'const f = x=>x; f<number>\n42;',
    'const f = x=>x; const g = f<number> /* / */ / 2; String(g);',
    'const a=1,b=2,c=3; a < b >> c;',
    'const a=1,b=2,c=3; a < b >>> c;',
    'const a=1,b=2,c=3; a < b > +c;',
    'const a=1,b=2,c=3; a < b > -c;',
    'const a=1,b=2,c=3; a < b >= c;',
    'const a=1,b=2,c=3; a < b > [c];',
    'const a=1,b=2,c=3; a < b / c > (c);',
    'class Base {f<T>(x:T):T {return x;}} class C extends Base {override f<T>(x:T):T {return super.f<T>(x);}} new C().f<number>(42);',
    'class C {readonly #value: number = 42; get value(): number {return this.#value;}} new C().value;',
    'class C {static?():number {return 42;}} new C().static();',
    'const f = <T>(x:T)=>x; (f<number>)(42);',
    'const f = <T>(x:T)=>x; (f<number>).call(null,42);',
    'const f = async<T>(x)=>x; f<number>(42);',
    'const f = async<T>(x=42)=>x; f<number>();',
    'const f = async<T>(x:number)=>x; f<number>(42);'
  ];
  focused.forEach((source,i)=>cases.push({name:`generics-focused-${i}`,source}));
  const comparisons = [
    '1 < f<T>(42) < f<U>(43)', '1 < (f<T>(42))', '1 < (2 < f<T>(42))',
    '1 < [f<T>(42)][0]', '1 < (f<T>(42) + f<U>(2))', '1 < f<T>(42) + f<U>(2)',
    '1 < 2 < f<T>(42) < 3 < f<U>(43)', '1 < f<T>(42) / f<U>(2)',
    '1 < (true ? f<T>(42) : f<U>(0))', '1 < (f<T>(42), f<U>(43))',
    '1 < f<T>(42) && 1 < f<U>(43)', '1 < f<T>(42) > f<U>(43)',
    '1 < f<T>(42) >> f<U>(2)', '1 < f<T>(42) >>> f<U>(2)',
    '1 < f<T>(42) >= f<U>(43)', '1 < f<T>(42) > +f<U>(43)',
    '1 < f<T>(42) > -f<U>(43)', '1 < f<T>(42) > [f<U>(43)]',
    '1 < f<A<B> & C>(42)', '1 < f<{value:A<B>}>(42)',
    '1 < (f<<T>()=>T>(42))', '1 < (f<`value${number}`>(42))'
  ];
  comparisons.forEach((e,i)=>cases.push({name:`generics-comparison-${i}`,source:`const f = x=>x; ${e};`}));
  ['<=', '<<', '==', '!=', '===', '!==', '+', '-', '*', '/', '**', '&', '|', '^', '&&', '||', '??']
    .forEach((op,i)=>cases.push({name:`generics-boundary-${i}`,source:`const f = 42, T = 2; f<T> ${op} 2;`}));
  ['<=', '<<', '+', '-', '/', '*']
    .forEach((op,i)=>cases.push({name:`generics-new-boundary-${i}`,source:`class C {valueOf() {return 42;}} const T = 2; new C<T> ${op} 2;`}));
  [
    'const f=42, g=x=>x; (f<T>) < g<U>(43);',
    'const f=42; f<T> in {42:true};',
    'const f=42; f<T> instanceof Number;'
  ].forEach((source,i)=>cases.push({name:`generics-instantiation-${i}`,source}));
  ['<', '>', '<=', '>=', '<<', '>>', '>>>'].forEach((op,i)=>cases.push({
    name:`generics-grouped-instantiation-${i}`,source:`const f=42; (f<T>) ${op} 2;`}));
  ['export class C<T> {public value: T = 42;}', 'export const f = <T>(x:T):T => x;',
    'export default class<T> {value: T = 42;}', 'export const f = async <T>(x:T):Promise<T> => x;',
    'const f = x=>x; export const g = f<number>;', 'export default <T>(x:T):T => x;']
    .forEach((source,i)=>cases.push({name:`generics-module-${i}`,source,module:true}));
  const originals = [
    'const f = < T extends A < number > = B > ( x : T ) : T => x ;',
    'const f = async < T > ( x : T = 42 ) : T => x ;',
    'const value = f < A < number > , { x : T } > ( 42 ) ;',
    'const value = f ?. < number > ( 42 ) ;',
    'class C < T > extends Base < T > implements A < T > { public readonly value : T = 42 ; f < U > ( x : U ) : U { return x ; } }'
  ];
  const tokens = ['<','>','[',']','{','}','(',')','?',':',';','extends','implements','readonly','public','=','=>','!',',','async','static','number'];
  let seed=0x7192124;
  const next=n=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%n;};
  const invalid = new Set();
  for(let i=0;i<7000;i++) {
    const parts=originals[next(originals.length)].split(' ');
    parts.splice(next(parts.length),next(2),tokens[next(tokens.length)]);
    const source=parts.join(' ');
    try {parse(source,{plugins:['typescript']});} catch {invalid.add(source);}
  }
  return {cases,invalid:[...invalid].sort().map((source,i)=>({name:`generics-invalid-${i}`,source}))};
}
