namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallBoxedPrototypeBuiltins()
    {
        InstallFunctionPrototypeBuiltins();
        InstallNumberPrototypeBuiltins();
        InstallBooleanPrototypeBuiltins();
        InstallStringPrototypeBuiltins();
        InstallBigIntPrototypeBuiltins();
        InstallSymbolPrototypeBuiltins();
    }
}
