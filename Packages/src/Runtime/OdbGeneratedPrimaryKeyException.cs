using System;

namespace oojjrs.odb
{
    internal sealed class OdbGeneratedPrimaryKeyException : InvalidOperationException
    {
        internal OdbGeneratedPrimaryKeyException(string message) : base(message)
        {
        }
    }
}
