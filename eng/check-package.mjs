import { mkdtempSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { basename, dirname, resolve } from 'node:path';

const packageFile = resolve(process.argv[2] ?? 'artifacts/packages/Jint.TypeScript.0.1.0-preview.1.nupkg');
const version = /^Jint\.TypeScript\.(.+)\.nupkg$/.exec(basename(packageFile))?.[1];
if (!version) throw new Error('Pass a Jint.TypeScript .nupkg filename.');
const directory = mkdtempSync(resolve(dirname(packageFile), 'consumer-'));
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
writeFileSync(resolve(directory, 'Consumer.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFrameworks>net8.0;net10.0</TargetFrameworks><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Jint.TypeScript" Version="${xml(version)}" /></ItemGroup>
</Project>`);
// An isolated package directory proves the just-packed artifact is used, even when this version is cached elsewhere.
writeFileSync(resolve(directory, 'NuGet.Config'), `<configuration>
  <packageSources><clear/><add key="local" value="${xml(dirname(packageFile))}"/><add key="Jint-preview" value="https://f.feedz.io/sebastienros/jint/nuget/index.json"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/><packageSource key="local"><package pattern="Jint.TypeScript"/></packageSource><packageSource key="Jint-preview"><package pattern="Jint"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>`);
writeFileSync(resolve(directory, 'Program.cs'), `using System.IO.Compression;
using System.Xml.Linq;
using Jint;
using Jint.TypeScript;

var compiler = new TypeScriptCompiler();
var prepared = compiler.PrepareScript("const answer: number = 42; answer;", "example.ts");
if (new Engine().Evaluate(prepared).AsNumber() != 42) throw new Exception("Script failed.");
var loader = new TypeScriptModuleLoader(new Dictionary<string, string>
{
    ["main.ts"] = "import { value } from './value.ts'; export const answer: number = value + 2;",
    ["value.ts"] = "export const value: number = 40;"
}, compiler);
if (new Engine(options => options.UseModules(loader)).Modules.Import("./main.ts").Get("answer").AsNumber() != 42)
    throw new Exception("Module failed.");
using var package = ZipFile.OpenRead(args[0]);
foreach (var framework in new[] { "net8.0", "net10.0" })
{
    using var stream = package.GetEntry($"lib/{framework}/Jint.TypeScript.xml")!.Open();
    var docs = XDocument.Load(stream);
    if (!docs.Descendants("member").Any(member => ((string?) member.Attribute("name"))?.StartsWith("T:Jint.TypeScript.TypeScriptModuleLoader") == true))
        throw new Exception("Missing module loader IntelliSense documentation.");
}
Console.WriteLine($"Package consumer passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
`);
function run(command, args) {
    const result = spawnSync(command, args, { cwd: directory, stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`${command} failed (${result.status}).`);
}
run('dotnet', ['restore', 'Consumer.csproj', '--packages', resolve(directory, 'packages')]);
run('dotnet', ['build', 'Consumer.csproj', '-c', 'Release', '--no-restore']);
for (const framework of ['net8.0', 'net10.0'])
    run(framework === 'net8.0' ? process.env.PACKAGE_DOTNET8 ?? 'dotnet' : 'dotnet',
        [resolve(directory, 'bin', 'Release', framework, 'Consumer.dll'), packageFile]);
