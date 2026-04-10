namespace Okojo.RegExp;

public interface IRegExpEngine
{
    RegExpCompiledPattern Compile(string pattern, string flags);
    RegExpMatchResult? Exec(RegExpCompiledPattern compiled, string input, int startIndex);
}
