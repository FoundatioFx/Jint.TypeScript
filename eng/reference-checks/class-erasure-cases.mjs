export function classErasureCases(parse) {
  const cases = [];
  const add = (name, sources, module = false) => sources.forEach((source, i) =>
    cases.push({name:`class-erasure-${name}-${i}`, source, ...(module ? {module:true} : {})}));
  const types = ['number', 'readonly number[]', 'T extends infer U ? U : never',
    'import("missing").Shape', 'typeof import("missing")', 'abstract new<const T>(x:T) => T'];
  const keys = ['value', '"value"', '42', '["value"]', '[key()]'];
  const modifiers = ['abstract', 'public abstract', 'protected abstract readonly', 'abstract public',
    'declare', 'public declare readonly', 'declare protected', 'declare static', 'static declare', 'declare abstract'];
  for (const [i, type] of types.entries()) for (const [j, key] of keys.entries()) for (const [k, modifier] of modifiers.entries()) {
    cases.push({name:`class-erasure-field-${i}-${j}-${k}`, source:
      `let calls=0; function key() {calls++; return 'value';} abstract class C<T> {${modifier} ${key}: ${type}; present=42;} new C().present + calls;`});
  }
  const methods = [
    'abstract f(x:T):T;', 'abstract f<const U extends T>(this:C<T>, x:U):U;',
    'public abstract f(x?:T):T;', 'protected abstract f(...xs:T[]):T;',
    'abstract f({value}: {value:T}):T;', 'abstract f([value]:[T]):T;',
    'abstract f(x:unknown):x is T;', 'abstract f(x:unknown):asserts x is T;',
    'abstract get value():T;', 'abstract set value(x:T);',
    'abstract [key()](x:T):T;', 'abstract "f"(x:T):T;',
    'abstract *f(x:T):T;', 'abstract async f(x:T):Promise<T>;',
    'abstract constructor(x:T);', 'abstract f?():T;',
    'abstract f():T; abstract f(x:T):T;', 'abstract get value(); abstract set value(x:T):void;'
  ];
  const wrappers = [
    m=>`abstract class C<T> {${m} present=42;} new C().present + calls;`,
    m=>`class B {present=42;} abstract class C<T> extends B {${m}} new C().present + calls;`,
    m=>`function build() {abstract class C<T> {${m} present=42;} return C;} new (build())().present + calls;`,
    m=>`{abstract class C<T> {${m} present=42;} new C().present + calls;}`
  ];
  for (const [i,m] of methods.entries()) for (const [j,wrap] of wrappers.entries())
    cases.push({name:`class-erasure-method-${i}-${j}`,source:`let calls=0; function key() {calls++; return 'value';} ${wrap(m)}`});
  add('behavior', [
    'class B {value=42;} class C extends B {declare value:number;} new C().value;',
    'class B {value=42;} abstract class C extends B {abstract value:number;} new C().value;',
    'class B {static value=42;} class C extends B {declare static value:number;} C.value;',
    'abstract class B {abstract value:number; answer=42;} class C extends B {value=2;} new C().answer;',
    'abstract class C {abstract value:number; normal:number; declare other:number;} const c=new C(); !Object.hasOwn(c,"value") && Object.hasOwn(c,"normal") && !Object.hasOwn(c,"other");',
    'let calls=0; abstract class B {abstract [++calls]:number; abstract [++calls]():number; declare [++calls]:number;} new B(); calls;',
    'let calls=0; abstract class B {static {calls++;} constructor() {calls++;} abstract f():number;} new B(); calls;',
    'abstract class C<T> {abstract get value():T; abstract set value(v:T);} class D extends C<number> {get value() {return 42;}} new D().value;',
    'class C {abstract=40; declare=2;} const c=new C(); c.abstract+c.declare;',
    'class C {abstract() {return 40;} declare() {return 2;}} const c=new C(); c.abstract()+c.declare();',
    'class C {abstract:number=40; declare:number=2;} const c=new C(); c.abstract+c.declare;',
    'class C {abstract\nvalue:number; declare\nother:number;} Object.keys(new C()).join(",");',
    'const abstract=40; abstract\nclass C {value=2;} abstract+new C().value;',
    'abstract class C {} class D {f() {class C {abstract=42;} return new C().abstract;}} new D().f();',
    'abstract class B {abstract overrideName():number;} abstract class C extends B {abstract override overrideName():number;}',
    'abstract class C {abstract x!:number; declare y!:number; declare z?; abstract a;} Object.keys(new C()).length;',
    'abstract class C {abstract f(x:number):number; f(x) {return x;}} new C().f(42);',
    'abstract class C {abstract f():number; g() {abstract class D {abstract x:number; value=42;} return new D().value;}} new C().g();',
    'abstract class C {abstract get value():number; value2=42;} Object.getOwnPropertyDescriptor(C.prototype,"value") === undefined;',
    'type Factory=abstract new<const T>(x:T)=>T; const id=x=>x; id<Factory>(42);',
    'type Factory=abstract\nnew()=>object; 42;',
    'type Factory=abstract new()=>T extends infer U ? U:never; 42;',
    'type T=X extends abstract new()=>infer U ? U:never; 42;',
    'function f(x:abstract new()=>object) {return 42;} f(null);',
    'const f=(x):abstract new()=>object=>x; f(42);',
    'type T=(abstract new()=>object) | (new()=>object); 42;'
  ]);
  add('modules', [
    'export abstract class C<T> {abstract value:T; declare host:import("missing").Host; answer=42;}',
    'export default abstract class {abstract f():number; answer=42;}',
    'export default abstract class C {abstract get value():number; abstract set value(v:number); answer=42;}',
    'abstract class C {abstract f():number;} export {C};',
    'export default abstract class extends Object {declare value:number; answer=42;}',
    'export default class C {declare value:number; answer=42;}'
  ], true);
  const originals = [
    'abstract class C { abstract value : number ; answer = 42 ; }',
    'class C { declare value : number ; answer = 42 ; }',
    'abstract class C { abstract f < const T > ( x : T ) : T ; }',
    'abstract class C { abstract get value ( ) : number ; abstract set value ( x : number ) ; }',
    'class B { value = 42 ; } class C extends B { declare value : number ; }',
    'abstract class C { abstract [ key ( ) ] ( x : number ) : number ; }',
    'type T = abstract new < const T > ( x : T ) => T ;'
  ];
  const tokens = ['abstract','declare','class','readonly','private','public','protected','static','override','async',
    'get','set','const','extends','implements','new','number','x','constructor','prototype','#x','*',
    '<','>','(',')','[',']','{','}','?',':',';','=',',','=>','!','\n'];
  let seed=0x19240909;
  const next=n=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%n;};
  const invalid=new Set();
  for(let i=0;i<18000;i++) {
    const parts=originals[next(originals.length)].split(' ');
    parts.splice(next(parts.length),next(2),tokens[next(tokens.length)]);
    const source=parts.join(' ');
    try {parse(source,{plugins:['typescript']});} catch {invalid.add(source);}
  }
  return {cases,invalid:[...invalid].sort().map((source,i)=>({name:`class-erasure-invalid-${i}`,source}))};
}
