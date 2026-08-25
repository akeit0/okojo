param()
$a = (Get-Content "test262/test/built-ins/Array/prototype/shift/S15.4.4.9_A1.2_T1.js" -Raw) -replace '(?s).*?---\*/\r?\n', ''
# Normalize: strip comments and blank lines so only real statements remain.
$stripComments = {
    param($text)
    # Remove block comments (non-greedy, multiline).
    $t = [regex]::Replace($text, '/\*.*?\*/', '', 'Singleline')
    # Remove line comments.
    $t = [regex]::Replace($t, '//[^\n]*', '')
    # Collapse blank lines.
    $t = ($t -split "`n" | Where-Object { $_.Trim().Length -gt 0 }) -join "`n"
    return $t
}
$fa = & $stripComments $a
$fb = & $stripComments ((Get-Content "test262/test/language/temp-shift-probe.js" -Raw) -replace "`r", '')
"lenA=$($fa.Length) lenB=$($fb.Length)"
$min = [Math]::Min($fa.Length, $fb.Length)
for ($i = 0; $i -lt $min; $i++) {
    if ($fa[$i] -ne $fb[$i]) {
        "first divergence at char $i"
        "A: " + $fa.Substring([Math]::Max(0, $i - 40), [Math]::Min(90, $fa.Length - [Math]::Max(0, $i - 40)))
        "B: " + $fb.Substring([Math]::Max(0, $i - 40), [Math]::Min(90, $fb.Length - [Math]::Max(0, $i - 40)))
        break
    }
}
else { "identical after comment-strip: len=$min" }
