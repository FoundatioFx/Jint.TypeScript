using System;

namespace Jint.TypeScript.Parsing;

internal sealed class SyntaxErrorException : ParseErrorException
{
    public SyntaxErrorException(SyntaxError error, Exception? innerException = null)
        : base(error, innerException) { }

    public new SyntaxError Error => (SyntaxError)base.Error;
}
