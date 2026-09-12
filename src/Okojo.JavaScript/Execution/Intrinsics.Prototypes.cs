namespace Okojo.JavaScript.Execution;

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
