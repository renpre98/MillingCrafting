// Unity 6's UnityEngine.CoreModule exposes a public System.Runtime.CompilerServices.NullableAttribute
// without the constructors the C# compiler needs (CS0656). Declaring them in this assembly makes the
// compiler use these instead.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Event | AttributeTargets.Field | AttributeTargets.GenericParameter | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public readonly byte[] NullableFlags;
        public NullableAttribute(byte value) => NullableFlags = new[] { value };
        public NullableAttribute(byte[] value) => NullableFlags = value;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public readonly byte Flag;
        public NullableContextAttribute(byte value) => Flag = value;
    }
}
