const cases = [
  { id: 'literal-search', pattern: 'abc', flags: '', input: 'xxabczz' },
  { id: 'alternation-priority', pattern: '(a|ab)', flags: '', input: 'ab' },
  { id: 'greedy-captures', pattern: '(a+)(a)', flags: '', input: 'aaaa' },
  { id: 'lazy-quantifier', pattern: 'a+?', flags: '', input: 'aaa' },
  { id: 'named-capture', pattern: '(?<word>[A-Za-z]+)-(\\d+)', flags: '', input: 'xxAb-42' },
  { id: 'numbered-backref', pattern: '(a|b)\\1', flags: '', input: 'xbb' },
  { id: 'forward-backref', pattern: '\\1(a)', flags: '', input: 'a' },
  { id: 'named-backref', pattern: '(?<x>a|b)\\k<x>', flags: '', input: 'xaa' },
  { id: 'lookahead-capture', pattern: '(?=(a+))a*b\\1', flags: '', input: 'baabac' },
  { id: 'negative-lookahead-capture', pattern: '(.*?)a(?!(a+)b\\2c)\\2(.*)', flags: '', input: 'baaabaac' },
  { id: 'reverse-lookbehind-captures', pattern: '(?<=([ab]+)([bc]+))$', flags: '', input: 'abc' },
  { id: 'variable-lookbehind', pattern: '(?<=a{1,3})b', flags: '', input: 'xaaab' },
  { id: 'negative-lookbehind', pattern: '(?<!a)b', flags: '', input: 'cb' },
  { id: 'multiline-anchor', pattern: '^b$', flags: 'm', input: 'a\nb\nc' },
  { id: 'dotall', pattern: 'a.b', flags: 's', input: 'a\nb' },
  { id: 'astral-no-u', pattern: '^.$', flags: '', input: '😀' },
  { id: 'astral-u', pattern: '^.$', flags: 'u', input: '😀' },
  { id: 'unicode-word-kelvin', pattern: '\\bK\\b', flags: 'iu', input: ' K ' },
  { id: 'unicode-property-greek', pattern: '\\p{Script=Greek}+', flags: 'u', input: 'xΩβy' },
  { id: 'unicode-property-letter', pattern: '^\\p{Letter}+$', flags: 'u', input: 'AΩ文' },
  { id: 'v-intersection', pattern: '[\\p{ASCII}&&\\p{Letter}]+', flags: 'v', input: '12AzΩ' },
  { id: 'v-subtraction', pattern: '[[A-Za-z]--[A-Z]]+', flags: 'v', input: 'AAabcZZ' },
  { id: 'empty-repeat-capture-empty', pattern: '(a?)*', flags: '', input: '' },
  { id: 'empty-repeat-capture-a', pattern: '(a?)*', flags: '', input: 'a' },
  { id: 'bounded-nullable-capture', pattern: '(a?){0,2}', flags: '', input: 'a' },
  { id: 'optional-unmatched', pattern: '(a)?b', flags: '', input: 'b' },
  { id: 'annex-b-octal', pattern: '\\141+', flags: '', input: 'xaa' },
  { id: 'scoped-modifier', pattern: '(?i:a)b', flags: '', input: 'Ab' },
];

function serialize(c) {
  try {
    const re = new RegExp(c.pattern, c.flags);
    const match = re.exec(c.input);
    return {
      ...c,
      compiled: true,
      match: match === null ? null : {
        index: match.index,
        text: match[0],
        captures: Array.from(match, v => v === undefined ? null : v),
        groups: match.groups ?? null,
      },
    };
  } catch (error) {
    return { ...c, compiled: false, error: String(error?.message ?? error) };
  }
}

const syntaxCases = [
  { id: 'bad-u-identity', pattern: '\\q', flags: 'u' },
  { id: 'bad-quantifier-order', pattern: 'a{3,2}', flags: '' },
  { id: 'duplicate-name', pattern: '(?<x>a)(?<x>b)', flags: '' },
  { id: 'u-v-exclusive', pattern: 'a', flags: 'uv' },
  { id: 'bad-v-string', pattern: '[\\q{ab|cd}]', flags: 'v' },
];

const output = {
  node: process.version,
  v8: process.versions.v8,
  cases: cases.map(serialize),
  syntax: syntaxCases.map(c => {
    try {
      new RegExp(c.pattern, c.flags);
      return { ...c, compiled: true };
    } catch (error) {
      return { ...c, compiled: false, error: String(error?.message ?? error) };
    }
  }),
};

console.log(JSON.stringify(output, null, 2));
