namespace Okojo.Runtime;

internal static class ClrDisplay
{
    internal static string FormatTypeName(Type type)
    {
        if (type.IsByRef)
            return FormatTypeName(type.GetElementType()!) + "&";
        if (type.IsArray)
            return FormatTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter)
            return type.Name;

        if (type.IsGenericType)
        {
            var genericDefinition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
            var baseName = (genericDefinition.FullName ?? genericDefinition.Name).Replace('+', '.');
            var tickIndex = baseName.IndexOf('`');
            if (tickIndex >= 0)
                baseName = baseName[..tickIndex];

            var args = type.GetGenericArguments();
            var formattedArgs = new string[args.Length];
            for (var i = 0; i < args.Length; i++)
                formattedArgs[i] = FormatTypeName(args[i]);
            return $"{baseName}<{string.Join(", ", formattedArgs)}>";
        }

        return (type.FullName ?? type.Name).Replace('+', '.');
    }
}
