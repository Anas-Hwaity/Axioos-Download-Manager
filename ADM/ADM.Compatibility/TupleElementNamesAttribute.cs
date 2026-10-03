#if NET35


using System.Collections.Generic;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Event)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        private readonly string?[] _transformNames;

        public TupleElementNamesAttribute(string?[] transformNames)
        {
            if (transformNames == null)
            {
                throw new ArgumentNullException(nameof(transformNames));
            }

            _transformNames = transformNames;
        }

        public IList<string?> TransformNames => _transformNames;
    }
}

#endif