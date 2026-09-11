export function typeMemberCases(parse) {
  const cases = [];
  const keys = ['value', '"value"', '42', '[key]', '[Symbol.iterator]', '[Keys["value"]]'];
  const types = ['number', 'readonly T[]', 'T extends infer U ? U : never',
    'import("missing").Shape<T>', '({value}: Input) => number', '{[Symbol.iterator](): Iterator<T>}'];
  for (const [i, key] of keys.entries()) for (const [j, type] of types.entries()) {
    const members = `get ${key}(): ${type}; set ${key}(value: ${type});`;
    cases.push({name: `type-member-accessor-${i}-${j}`, source: `interface I<T> {${members}} 42;`});
    cases.push({name: `type-member-object-${i}-${j}`, source: `const f=(x): {${members}} => x; f(42);`});
  }
  const computed = ['[key]', '[Symbol.iterator]', '[Keys.default]', '[Keys[other.key]]', '[this.key]',
    '["value"]', '[42]', '[-1]', '[1n]', '[-1n]', '[true]', '[null]'];
  for (const [i, key] of computed.entries()) {
    for (const [j, member] of [`${key}: number;`, `readonly ${key}?: number;`,
      `${key}<T>({value}: {value:T}): T;`, `${key}?([value]: [number]): number;`].entries()) {
      cases.push({name:`type-member-computed-${i}-${j}`, source:`interface I {${member}} 42;`});
    }
  }
  const bindings = ['{value}', '[value]', '{}', '[]', '{value: renamed}', '{default: renamed, if: other}',
    '{"value": renamed, 42: other}', '{[key]: value, [Keys.value]: other}',
    '{value: {nested: [first,,...rest]}, ...other}', '[first,,...rest]', '[...[first,...rest]]',
    '[...{length}]', '{value,}', '[value,]', '[,]', '{value: [[{nested}]]}'];
  const contexts = [
    p => `type F=(${p}:Input)=>number; 42;`,
    p => `type F=<T>(this:Host, ${p}:Input<T>)=>T; 42;`,
    p => `type F=new (${p}:Input)=>Host; 42;`,
    p => `type F=abstract new (${p}:Input)=>Host; 42;`,
    p => `interface I {f(${p}:Input):number; (${p}:Input):number; new (${p}:Input):Host;} 42;`,
    p => `type T={set value(${p}:Input);}; 42;`,
    p => `const f=(x):(${p}:Input)=>number=>x; f(42);`,
    p => `function f<T>(x:T) {return x;} f<(${p}:Input)=>number>(42);`,
    p => `type T=X extends (${p}:Input)=>infer R ? R : never; 42;`,
    p => `type T=(${p}?:Input)=>number; 42;`,
    p => `type T=(...${p}:Input)=>number; 42;`
  ];
  for (const [i,p] of bindings.entries()) for (const [j,context] of contexts.entries())
    cases.push({name:`type-member-binding-${i}-${j}`,source:context(p)});
  const grouped = ['({value:number})', '([number,string])', '({nested:{value:[number,string]}})',
    '({value:`item-${number}`})', '({value:Array<Array<number>>})', '({[P in keyof T]:T[P]})',
    '({get value():number;set value(v:number)})', '({[Symbol.iterator]():Iterator<number>})',
    '(({value}:Input)=>number)', '(([value]:[number])=>number)',
    '({value:(x:number)=>{[Symbol.iterator]():Iterator<number>}})'];
  for (const [i,type] of grouped.entries()) {
    cases.push({name:`type-member-grouped-${i}`,source:`function f(x:${type}) {return 42;} f(null);`});
    cases.push({name:`type-member-return-${i}`,source:`function f(x):${type} {return x;} f(42);`});
  }
  const behavior = [
    'interface I {get: number;set():number;readonly:number;readonly get:number;} 42;',
    'interface I {get get():number;set set(value:number);} 42;',
    'interface I {get\nvalue():number;set /*comment*/ value(value:number);} 42;',
    'interface I {get value();set value(value);} 42;',
    'interface I {get value(): value is number;} 42;',
    'interface I {get value(): asserts this is T;} 42;',
    'interface I {[name:string]:number;[Symbol.iterator]():Iterator<number>;[name:number]:number;} 42;',
    'let reads=0; const keys={get key(){reads++;throw 1;}}; interface I {[keys.key]:number;get [missing.key]():number;} reads;',
    'const value=40, other=2; type F=({value}:Input,[other]:[number])=>number; value+other;',
    'function f(x:{get value():number}):number {return x.value;} f({get value(){return 42;}});',
    'type T={get x():number;}; /ok/.test("ok");',
    'const f=(x):{[Symbol.iterator]():Iterator<T>}=>x; f(42);',
    'const f=<T extends {get value():number}>(x:T)=>x; f<number>(42);',
    'const f=<T extends {[Symbol.iterator]():Iterator<T>}>(x:T)=>x; f<number>(42);',
    'type F=({value},[other],)=>number; 42;',
    'type F=({value}:Input,{value}:Input)=>number; 42;',
    'type F=({eval}:Input,{arguments}:Input)=>number; 42;',
    'type F=({value}:Input)=>T extends U ? X:Y; 42;',
    'type F=({value}:Input)=>({other}:Input)=>number; 42;'
  ];
  behavior.forEach((source,i)=>cases.push({name:`type-member-behavior-${i}`,source}));
  for (const [i,source] of [
    'export interface I {get value():number;set value(v:number);[Symbol.iterator]():Iterator<number>;}',
    'export type F=({value}:Input)=>number;',
    'export interface I {get [missing.key]():number;} export const answer:number=42;',
    'import type {Input} from "missing";export type F=({value}:Input)=>number;export const answer=42;',
    'declare interface I {get value():number;} export const answer=42;'
  ].entries()) cases.push({name:`type-member-module-${i}`,source,module:true});

  const originals = [
    'interface I { get value ( ) : number ; set value ( value : number ) ; }',
    'type I = { get [ Symbol . iterator ] ( ) : number ; set [ key ] ( value : number ) ; } ;',
    'interface I { readonly [ key ] ? : number ; [ name : string ] : number ; }',
    'interface I { [ Symbol . iterator ] < T > ( { value } : Input < T > ) : T ; }',
    'type F = ( { value : [ first , , ... rest ] , ... other } : Input ) => number ;',
    'type F = ( [ ... [ value , ... rest ] ] : Input ) => number ;',
    'type F = < T > ( this : Host , { [ key ] : value } : Input ) => T ;',
    'const f = ( x ) : ( { value } : Input ) => number => x ; f ( 42 ) ;'
  ];
  const tokens = ['get','set','readonly','new','this','typeof','in','out','const','key','value','number',
    'null','true','false','if','yield','await','...','<','>','(',')','[',']','{','}','?',':',';',
    '=',',','=>','.','?.','+','++','!','\n','"value"','42','#key'];
  let seed=0x19260910;
  const next=n=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)%n;};
  const invalid=new Set();
  for(let i=0;i<18000;i++) {
    const parts=originals[next(originals.length)].split(' ');
    parts.splice(next(parts.length),next(2),tokens[next(tokens.length)]);
    const source=parts.join(' ');
    try {parse(source,{plugins:['typescript']});} catch {invalid.add(source);}
  }
  return {cases,invalid:[...invalid].sort().map((source,i)=>({name:`type-member-invalid-${i}`,source}))};
}
