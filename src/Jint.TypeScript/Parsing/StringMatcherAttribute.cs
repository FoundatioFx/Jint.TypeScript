using System;

namespace Jint.TypeScript.Parsing;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class StringMatcherAttribute : Attribute
{
    public StringMatcherAttribute(params string[] targets)
    {
        Targets = targets;
    }

    public string[] Targets { get; }
}
