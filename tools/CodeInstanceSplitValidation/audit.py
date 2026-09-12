#!/usr/bin/env python3
"""Narrow source-structure checks. This is NOT a C# compiler or test runner."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
import re

def mask(s):
    out=list(s); i=0; n=len(s)
    def blank(a,b):
        for k in range(a,b):
            if out[k]!='\n':out[k]=' '
    while i<n:
        a=i
        if s.startswith('//',i):
            e=s.find('\n',i); i=n if e<0 else e; blank(a,i); continue
        if s.startswith('/*',i):
            e=s.find('*/',i+2); i=n if e<0 else e+2; blank(a,i);continue
        if s[i] in "\"'":
            quote=s[i]
            m=re.match(r'"{3,}',s[i:]) if quote=='"' else None
            if m:
                delim=m.group();e=s.find(delim,i+len(delim));i=n if e<0 else e+len(delim);blank(a,i);continue
            verbatim=quote=='"' and i>0 and s[i-1]=='@'
            i+=1
            while i<n:
                if s[i]==quote:
                    if verbatim and i+1<n and s[i+1]==quote:i+=2;continue
                    i+=1;break
                if s[i]=='\\' and not verbatim:i+=2
                else:i+=1
            blank(a,i);continue
        i+=1
    return ''.join(out)

def closing(s,start,op='(',cl=')'):
    m=mask(s); d=0
    for i in range(start,len(s)):
        if m[i]==op:d+=1
        elif m[i]==cl:
            d-=1
            if d==0:return i
    raise ValueError(('Unbalanced',start,s[start:start+80]))

def split_args(s):
    m=mask(s); stack=[];last=0;out=[]
    for i,c in enumerate(m):
        if c in '([{':stack.append(c)
        elif c in ')]}':stack.pop()
        elif c==',' and not stack:
            if s[last:i].strip():out.append(s[last:i].strip())
            last=i+1
    if s[last:].strip():out.append(s[last:].strip())
    return out


def main() -> int:
    checks: list[dict[str, object]] = []

    def check(name: str, passed: bool, detail: str = '') -> None:
        checks.append({'name': name, 'passed': bool(passed), 'detail': detail})

    manifest = Path(__file__).with_name('changed-csharp.txt')
    changed = [ROOT / name for name in manifest.read_text().splitlines() if name]
    delimiter_errors = []
    for path in changed:
        source = mask(path.read_text())
        stack: list[tuple[str, int]] = []
        for index, char in enumerate(source):
            if char in '([{':
                stack.append((char, index))
            elif char in ')]}':
                if not stack or stack[-1][0] != {')': '(', ']': '[', '}': '{'}[char]:
                    delimiter_errors.append(f'{path.relative_to(ROOT)}:{source.count(chr(10), 0, index) + 1}')
                    break
                stack.pop()
        else:
            if stack:
                delimiter_errors.append(f'{path.relative_to(ROOT)}:unclosed delimiter')
    check('Changed C# delimiter balance (lexical check only)', not delimiter_errors,
          f'{len(changed)} files; errors={delimiter_errors}')

    source_files = list((ROOT / 'src').rglob('*.cs'))
    stale = []
    for path in source_files:
        text = mask(path.read_text())
        if re.search(r'\b(?:CloneForClosure|BindAgent|AllocatePrivateBrandId)\s*\(', text):
            stale.append(str(path.relative_to(ROOT)))
    check('Removed ownership/clone APIs have no production callers', not stale, str(stale))

    compiler = ROOT / 'src/Okojo.JavaScript/Compiler'
    coupled = []
    for path in compiler.glob('*.cs'):
        if re.search(r'\.(?:Atoms|EmptyShape|GlobalObject|GlobalLexicalBindings)\b', mask(path.read_text())):
            coupled.append(path.name)
    check('Compiler does not resolve live realm atoms/shapes/globals', not coupled, str(coupled))

    bytecode = ROOT / 'src/Okojo.JavaScript/Bytecode'
    function = (ROOT / 'src/Okojo.JavaScript/Objects/JsBytecodeFunction.cs').read_text()
    check('Closure construction does not clone runtime object state',
          'MemberwiseClone' not in function and 'CloneForClosure' not in function)
    code = mask((bytecode / 'JsFunctionCode.cs').read_text())
    check('Portable code public array views are read-only spans',
          'public ReadOnlySpan<byte> Bytecode' in code and not re.search(r'public\s+\w+\[\]', code))
    check('Portable function code has no realm/agent/shape fields',
          not re.search(r'\b(?:JsRealm|JsAgent|StaticNamedPropertyLayout)\s+\w+', code))

    runtime = mask((bytecode / 'RuntimeId.cs').read_text())
    body = runtime[runtime.index('{') + 1:runtime.rindex('}')]
    runtime_ids = [part.strip().split('=')[0].strip() for part in body.split(',') if part.strip()]
    handlers_source = mask((ROOT / 'src/Okojo.JavaScript/Execution/JsRealm.Vm.cs').read_text())
    handler_start = handlers_source.index('[', handlers_source.index('SRuntimeHandlers ='))
    handler_end = closing(handlers_source, handler_start, '[', ']')
    handlers = [item.strip() for item in handlers_source[handler_start + 1:handler_end].split(',') if item.strip()]
    check('Runtime ID / handler table lengths agree', len(runtime_ids) == len(handlers),
          f'IDs={len(runtime_ids)}, handlers={len(handlers)}')
    check('New runtime helper is appended in both tables',
          runtime_ids[-1] == 'DeleteGlobalBinding' and handlers[-1] == 'HandleRuntimeDeleteGlobalBinding')
    vm_loop = (ROOT / 'src/Okojo.JavaScript/Execution/JsRealm.VmLoop.cs').read_text()
    check('VM frame hoists execution view and numeric constants',
          'currentFunc.Script.ExecutionBytecode' in vm_loop
          and 'var numericConstants = currentFunc.Script.NumericConstants;' in vm_loop)

    report = {
        'scope': 'Source structure only; does not establish C# syntax, type correctness, runtime correctness, or performance',
        'passed': all(item['passed'] for item in checks),
        'checks': checks,
    }
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
