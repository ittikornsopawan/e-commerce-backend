namespace Ecommerce.Application;

/// <summary>Handle for assembly scanning, for example <c>AddCqrs(typeof(AssemblyReference).Assembly)</c>.</summary>
public static class AssemblyReference
{
    public static readonly System.Reflection.Assembly Assembly = typeof(AssemblyReference).Assembly;
}
