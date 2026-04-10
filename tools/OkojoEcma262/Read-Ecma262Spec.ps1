param(
  [Parameter(Position = 0)]
  [string]$Target = "",

  [string]$IndexUrl = "https://tc39.es/ecma262/multipage/",

  [string]$SectionUrl = "",

  [string]$OutDir = "artifacts/ecma262",

  [switch]$Catalog,

  [Alias("h", "help")]
  [switch]$ShowHelp,

  [ValidateSet("Auto", "Pandoc", "Node", "Bun", "Python", "Remote")]
  [string]$PreferredConverter = "Auto",

  [switch]$ForceRefresh,

  [switch]$Open
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

function Get-AbsoluteUrl {
  param(
    [string]$BaseUrl,
    [string]$RelativeUrl
  )

  $base = [uri]$BaseUrl
  if ([string]::IsNullOrWhiteSpace($RelativeUrl)) {
    return $base.AbsoluteUri
  }

  if ($RelativeUrl -like "http*") {
    return $RelativeUrl
  }

  $combined = New-Object uri($base, $RelativeUrl.TrimStart("./"))
  return $combined.AbsoluteUri
}

function Get-PageNameFromUri {
  param([uri]$Uri)

  $name = [System.IO.Path]::GetFileNameWithoutExtension($Uri.LocalPath)
  if ([string]::IsNullOrWhiteSpace($name) -or $name -eq "multipage") {
    return "index"
  }

  return $name
}

function Resolve-SectionUrl {
  param(
    [string]$SectionId,
    [string]$SectionUrlInput,
    [string]$IndexUrl
  )

  if (-not [string]::IsNullOrWhiteSpace($SectionUrlInput)) {
    return $SectionUrlInput
  }

  $normalizedSection = Normalize-SectionId -SectionId $SectionId
  $indexHtml = Invoke-WebRequest -Uri $IndexUrl -UseBasicParsing -ErrorAction Stop

  $pattern = "(?i)href=`"([^`"]*#([^`"]*${[regex]::Escape($normalizedSection)}[^`"]*))`""
  $match = [regex]::Match($indexHtml.Content, $pattern)
  if (-not $match.Success) {
    throw "Could not resolve section '$SectionId' from index page. Verify section id and inspect: $IndexUrl"
  }

  $href = $match.Groups[1].Value
  return Get-AbsoluteUrl -BaseUrl $IndexUrl -RelativeUrl $href
}

function Get-SectionContext {
  param(
    [string]$Section,
    [uri]$ResolvedUri,
    [string]$CacheDir,
    [string]$MarkdownDir
  )

  $sectionId = $ResolvedUri.Fragment.TrimStart("#")
  if ([string]::IsNullOrWhiteSpace($sectionId)) {
    $sectionId = $Section
  }

  $sectionId = Normalize-SectionId -SectionId $sectionId

  $pageName = Get-PageNameFromUri -Uri $ResolvedUri
  $cachePageDir = Join-Path $CacheDir $pageName
  $markdownPageDir = Join-Path $MarkdownDir $pageName
  $markdownPath = Get-SectionMarkdownPath -SectionId $sectionId -MarkdownPageDir $markdownPageDir

  return [PSCustomObject]@{
    SectionId = $sectionId
    PageName = $pageName
    CachePageDir = $cachePageDir
    MarkdownPageDir = $markdownPageDir
    HtmlPath = Join-Path $cachePageDir "$sectionId.html"
    MarkdownPath = $markdownPath
    RawPagePath = Join-Path $cachePageDir "$pageName.html"
    Reference = $ResolvedUri.AbsoluteUri
  }
}

function Decode-SectionId {
  param([string]$SectionId)

  if ([string]::IsNullOrWhiteSpace($SectionId)) {
    return ""
  }

  $trimmed = $SectionId.Trim().TrimStart("#")
  try {
    return [uri]::UnescapeDataString($trimmed)
  } catch {
    return $trimmed
  }
}

function Normalize-SectionId {
  param([string]$SectionId)

  if ([string]::IsNullOrWhiteSpace($SectionId)) {
    return ""
  }

  $decoded = Decode-SectionId -SectionId $SectionId
  if ($decoded -like "sec-*") {
    return $decoded
  }

  # Keep arbitrary fragment ids as-is when they are clearly not shorthand section names.
  if ($decoded -match '[^A-Za-z0-9._-]' -or $decoded.Length -le 3) {
    return $decoded
  }

  return "sec-$decoded"
}

function Get-SectionIdCandidates {
  param([string]$SectionId)

  $decoded = Decode-SectionId -SectionId $SectionId
  $raw = $SectionId.Trim().TrimStart("#")
  $candidates = [System.Collections.Generic.List[string]]::new()

  foreach ($candidate in @($decoded, $raw)) {
    if (-not [string]::IsNullOrWhiteSpace($candidate) -and -not $candidates.Contains($candidate)) {
      $candidates.Add($candidate)
    }
  }

  if ($decoded -notlike "sec-*") {
    $prefixed = "sec-$decoded"
    if (-not $candidates.Contains($prefixed)) {
      $candidates.Add($prefixed)
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($raw) -and $raw -notlike "sec-*") {
    $legacyPrefixedRaw = "sec-$raw"
    if (-not $candidates.Contains($legacyPrefixedRaw)) {
      $candidates.Add($legacyPrefixedRaw)
    }
  }

  return @($candidates)
}

function Remove-StaleSectionMarkdownAliases {
  param(
    [string[]]$SectionIds,
    [string]$ActualSectionId,
    [string]$ResolvedUri,
    [string]$CacheDir,
    [string]$MarkdownDir
  )

  foreach ($candidate in ($SectionIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)) {
    if ($candidate -eq $ActualSectionId) {
      continue
    }

    $candidateContext = Get-SectionContext -Section $candidate -ResolvedUri ([uri]$ResolvedUri) -CacheDir $CacheDir -MarkdownDir $MarkdownDir
    if (Test-Path $candidateContext.MarkdownPath) {
      Remove-Item -LiteralPath $candidateContext.MarkdownPath -Force -ErrorAction SilentlyContinue
    }

    $literalCandidatePath = Join-Path $MarkdownDir "$(Get-SafePathPart $candidate).md"
    if (Test-Path $literalCandidatePath) {
      Remove-Item -LiteralPath $literalCandidatePath -Force -ErrorAction SilentlyContinue
    }
  }
}

function Get-SafePathPart {
  param([string]$Value)

  $sanitized = $Value
  [System.IO.Path]::GetInvalidFileNameChars() | ForEach-Object {
    $sanitized = $sanitized.Replace($_, "_")
  }
  if ([string]::IsNullOrWhiteSpace($sanitized)) {
    return "_"
  }
  return $sanitized
}

function Get-SectionMarkdownPath {
  param(
    [string]$SectionId,
    [string]$MarkdownPageDir
  )

  $sectionId = Normalize-SectionId -SectionId $SectionId
  if ($sectionId -like "sec-*") {
    $sectionBody = $sectionId.Substring(4)
    if ([string]::IsNullOrWhiteSpace($sectionBody)) {
      return Join-Path $MarkdownPageDir "$sectionId.md"
    }

    $segments = $sectionBody -split '\.'
    $safeSegments = $segments | ForEach-Object { Get-SafePathPart $_ }
    if ($safeSegments.Count -eq 1) {
      return Join-Path $MarkdownPageDir "$sectionId.md"
    }

    $leaf = "sec-$($safeSegments[-1]).md"
    $folders = $safeSegments[0..($safeSegments.Count - 2)] | ForEach-Object { "sec-$_" }
    return Join-Path (Join-Path $MarkdownPageDir ($folders -join [System.IO.Path]::DirectorySeparatorChar)) $leaf
  }

  return Join-Path $MarkdownPageDir "$(Get-SafePathPart $sectionId).md"
}

function Test-SectionHtmlCache {
  param([string]$HtmlPath)

  if (-not (Test-Path $HtmlPath)) {
    return $false
  }

  $info = Get-Item -Path $HtmlPath
  if ($info.Length -lt 64) {
    return $false
  }

  $html = Get-Content -Raw -Path $HtmlPath
  return ($html -match '(?i)id\s*=\s*(?:"[^"]+"|''[^'']+''|[^\s>]+)')
}

function Show-ToolHelp {
  @"
Usage:
  .\tools\OkojoEcma262\Read-Ecma262Spec.ps1
    list all top-level ECMA-262 multipage pages.

  .\tools\OkojoEcma262\Read-Ecma262Spec.ps1 <page>
    list section ids for <page> (example: numbers-and-dates).

  .\tools\OkojoEcma262\Read-Ecma262Spec.ps1 <page>#<section>
    download the page if needed, convert the section to markdown, and print the markdown path.

  .\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Catalog
    collect all multipage urls and write section indexes.

  .\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Section <id> [-SectionUrl <url>]
    legacy section mode (resolved against index).

Options:
  -SectionUrl            direct section URL override.
  -IndexUrl              default: https://tc39.es/ecma262/multipage/
  -OutDir                default: artifacts/ecma262
  -Catalog               refresh all top-page/section metadata.
  -ForceRefresh          force re-download and conversion.
  -PreferredConverter    Auto, Pandoc, Node, Bun, Python, Remote
  -Open                  open generated markdown.
  -h, --help             show this help.
"@
}

function Get-CatalogData {
  param([string]$IndexUrl)

  $indexHtml = Invoke-WebRequest -Uri $IndexUrl -UseBasicParsing -ErrorAction Stop
  $indexText = $indexHtml.Content
  $links = [regex]::Matches($indexText, '(?i)href="([^"]+)"')

  $pages = [System.Collections.Generic.List[string]]::new()
  $sectionsByPage = @{}

  foreach ($link in $links) {
    $rawHref = $link.Groups[1].Value

    if ($rawHref -notlike "*.html*" -and $rawHref -notlike "*.html#*") {
      continue
    }

    try {
      $resolvedHref = Get-AbsoluteUrl -BaseUrl $IndexUrl -RelativeUrl $rawHref
      $hrefUri = [uri]$resolvedHref
    } catch {
      continue
    }

    if ($hrefUri.LocalPath -notmatch "/multipage/[^/]+\.html$") {
      continue
    }

    $pageUrl = "$($hrefUri.Scheme)://$($hrefUri.Authority)$($hrefUri.AbsolutePath)"
    if (-not $pages.Contains($pageUrl)) {
      $pages.Add($pageUrl)
    }

    $pageName = Get-PageNameFromUri -Uri $hrefUri
    if (-not $sectionsByPage.ContainsKey($pageName)) {
      $sectionsByPage[$pageName] = [System.Collections.Generic.HashSet[string]]::new()
    }

    if ($hrefUri.Fragment -match "^#(sec-[A-Za-z0-9._-]+)") {
      [void]$sectionsByPage[$pageName].Add($matches[1])
    }
  }

  $indexPageLinks = $indexText | Select-String -Pattern 'href="[^"]+#sec-[^"]+"' -AllMatches |
    ForEach-Object { $_.Matches } |
    ForEach-Object { $_.Value } |
    Sort-Object -Unique

  $indexSectionIds = $indexText | Select-String -Pattern 'id="sec-[^"]+"' -AllMatches |
    ForEach-Object { $_.Matches } |
    ForEach-Object { $_.Value } |
    Sort-Object -Unique

  return [PSCustomObject]@{
    IndexText = $indexText
    PageUrls = ($pages | Sort-Object -Unique)
    SectionsByPage = $sectionsByPage
    SectionLinks = $indexPageLinks
    SectionIds = $indexSectionIds
  }
}

function Save-CatalogArtifacts {
  param(
    [string]$OutDir,
    [string]$IndexUrl
  )

  $indexDir = Join-Path $OutDir "index"
  $markdownDir = Join-Path $OutDir "markdown"

  New-Item -ItemType Directory -Path $indexDir, $markdownDir -Force | Out-Null
  $catalog = Get-CatalogData -IndexUrl $IndexUrl

  $pageListPath = Join-Path $indexDir "ecma262-page-urls.txt"
  $catalog.PageUrls | Set-Content -Path $pageListPath -Encoding UTF8

  foreach ($pageName in ($catalog.SectionsByPage.Keys | Sort-Object)) {
    $pageDir = Join-Path $markdownDir $pageName
    New-Item -ItemType Directory -Path $pageDir -Force | Out-Null
    $sectionPath = Join-Path $pageDir "sections.md"
    $sectionLines = @($catalog.SectionsByPage[$pageName] | Sort-Object -Unique)
    if ($sectionLines.Count -eq 0) {
      Set-Content -Path $sectionPath -Value @() -Encoding UTF8
      continue
    }
    $sectionLines = $sectionLines | ForEach-Object { $_ }
    Set-Content -Path $sectionPath -Value $sectionLines -Encoding UTF8
  }

  $indexHtmlPath = Join-Path $indexDir "ecma262-multipage-index.html"
  $indexLinksPath = Join-Path $indexDir "ecma262-section-links.txt"
  $indexIdsPath = Join-Path $indexDir "ecma262-section-ids.txt"
  Set-Content -Path $indexHtmlPath -Value $catalog.IndexText -Encoding UTF8
  $catalog.SectionLinks | Set-Content -Path $indexLinksPath -Encoding UTF8
  $catalog.SectionIds | Set-Content -Path $indexIdsPath -Encoding UTF8

  Write-Host "Catalog saved:"
  Write-Host "  page urls:     $pageListPath"
  Write-Host "  page count:    $($catalog.PageUrls.Count)"
  Write-Host "  section index: $indexLinksPath"
  Write-Host "  section ids:   $indexIdsPath"
  Write-Host "  index snapshot:$indexHtmlPath"

  $catalog.SectionsByPage.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name) -> $($_.Value.Count) sections"
  }
}

function Extract-SectionListFromHtml {
  param([string]$HtmlPath)

  if (-not (Test-Path $HtmlPath)) {
    return @()
  }

  $html = Get-Content -Raw -Path $HtmlPath
  return [regex]::Matches($html, '(?is)<[a-zA-Z][^>]*\sid\s*=\s*(?:"(?<id>[^"]+)"|''(?<id>[^'']+)''|(?<id>[^\s>]+))[^>]*>') |
    ForEach-Object { $_.Groups["id"].Value } |
    Where-Object { $_ -match "^sec-[A-Za-z0-9._-]+" } |
    Sort-Object -Unique
}

function Resolve-SectionIdFromHtml {
  param(
    [string]$HtmlPath,
    [string]$RequestedSectionId
  )

  if (-not (Test-Path $HtmlPath)) {
    return Normalize-SectionId -SectionId $RequestedSectionId
  }

  $html = Get-Content -Raw -Path $HtmlPath
  $allIds = [regex]::Matches($html, '(?is)<[a-zA-Z][^>]*\sid\s*=\s*(?:"(?<id>[^"]+)"|''(?<id>[^'']+)''|(?<id>[^\s>]+))[^>]*>') |
    ForEach-Object { $_.Groups["id"].Value }

  foreach ($candidate in (Get-SectionIdCandidates -SectionId $RequestedSectionId)) {
    if ($allIds -contains $candidate) {
      return $candidate
    }
  }

  return Normalize-SectionId -SectionId $RequestedSectionId
}

function Test-ScopedSectionMarkdown {
  param([string]$MarkdownPath)

  if (-not (Test-Path $MarkdownPath)) {
    return $false
  }

  $text = Get-Content -Raw -Path $MarkdownPath -ErrorAction SilentlyContinue
  if ([string]::IsNullOrWhiteSpace($text)) {
    return $false
  }

  $sampleLength = [Math]::Min(4096, $text.Length)
  $sample = $text.Substring(0, $sampleLength)

  return (($sample -match "(?m)^URL Source:\s+https://tc39\\.es/ecma262/multipage/") -and ($sample -match "(?m)^Markdown Content:"))
}

function Extract-SectionHtmlFromPage {
  param(
    [string]$HtmlPath,
    [string]$SectionId
  )

  if (-not (Test-Path $HtmlPath)) {
    return $null
  }

  $html = Get-Content -Raw -Path $HtmlPath
  $sectionStart = $null
  $sectionTagPattern = '(?is)<[a-zA-Z][^>]*\sid\s*=\s*(?:"(?<id>[^"]+)"|''(?<id>[^'']+)''|(?<id>[^\s>]+))[^>]*>'
  $sectionMatches = [regex]::Matches($html, $sectionTagPattern)
  foreach ($match in $sectionMatches) {
    if ($match.Groups["id"].Value -eq $SectionId) {
      $sectionStart = $match
      break
    }
  }

  if (-not $sectionStart.Success) {
    return $null
  }

  if ($SectionId -notlike "sec-*") {
    $paragraphOpen = $html.LastIndexOf("<p", $sectionStart.Index, [System.StringComparison]::OrdinalIgnoreCase)
    $paragraphClose = $html.IndexOf("</p>", $sectionStart.Index, [System.StringComparison]::OrdinalIgnoreCase)
    if ($paragraphOpen -ge 0 -and $paragraphClose -gt $sectionStart.Index) {
      return $html.Substring($paragraphOpen, ($paragraphClose + 4) - $paragraphOpen)
    }

    $tagNameMatch = [regex]::Match($sectionStart.Value, '^<(?<tag>[a-zA-Z0-9:-]+)')
    if ($tagNameMatch.Success) {
      $tagName = $tagNameMatch.Groups["tag"].Value
      $closeTag = "</$tagName>"
      $tagClose = $html.IndexOf($closeTag, $sectionStart.Index, [System.StringComparison]::OrdinalIgnoreCase)
      if ($tagClose -gt $sectionStart.Index) {
        return $html.Substring($sectionStart.Index, ($tagClose + $closeTag.Length) - $sectionStart.Index)
      }
    }

    return $sectionStart.Value
  }

  $sectionEnd = $html.Length
  for ($i = 0; $i -lt $sectionMatches.Count; $i++) {
    $candidate = $sectionMatches[$i]
    if ($candidate.Index -le $sectionStart.Index) {
      continue
    }

    if ($candidate.Groups["id"].Value -like "sec-*") {
      $sectionEnd = $candidate.Index
      break
    }
  }

  return $html.Substring($sectionStart.Index, $sectionEnd - $sectionStart.Index)
}

function Get-SectionDepth {
  param([string]$SectionId)

  $normalized = Normalize-SectionId -SectionId $SectionId
  if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized.Length -le 4) {
    return 0
  }

  return ([regex]::Matches($normalized.Substring(4), '\.')).Count
}

function Convert-HtmlFragmentToPlainText {
  param([string]$Html)

  if ([string]::IsNullOrWhiteSpace($Html)) {
    return ""
  }

  $text = $Html -replace '(?is)<[^>]+>', ' '
  $text = [System.Net.WebUtility]::HtmlDecode($text)
  $text = $text -replace '\s+', ' '
  return $text.Trim()
}

function Get-SectionMetadataMapFromHtml {
  param([string]$HtmlPath)

  $map = @{}
  if (-not (Test-Path $HtmlPath)) {
    return $map
  }

  $html = Get-Content -Raw -Path $HtmlPath
  $sectionTagPattern = '(?is)<[a-zA-Z][^>]*\sid\s*=\s*(?:"(?<id>[^"]+)"|''(?<id>[^'']+)''|(?<id>[^\s>]+))[^>]*>'
  $sectionMatches = [regex]::Matches($html, $sectionTagPattern)
  for ($i = 0; $i -lt $sectionMatches.Count; $i++) {
    $match = $sectionMatches[$i]
    $id = $match.Groups["id"].Value
    if ($id -notlike "sec-*") {
      continue
    }

    $sectionEnd = $html.Length
    for ($j = $i + 1; $j -lt $sectionMatches.Count; $j++) {
      $candidate = $sectionMatches[$j]
      if ($candidate.Groups["id"].Value -like "sec-*") {
        $sectionEnd = $candidate.Index
        break
      }
    }

    $fragment = $html.Substring($match.Index, $sectionEnd - $match.Index)
    $headingMatch = [regex]::Match($fragment, '(?is)<h1[^>]*>(?<text>.*?)</h1>')
    if (-not $headingMatch.Success) {
      continue
    }

    $headingHtml = $headingMatch.Groups["text"].Value
    $headingText = Convert-HtmlFragmentToPlainText -Html $headingHtml
    $secnumMatch = [regex]::Match($headingHtml, '(?is)<span[^>]*class="secnum"[^>]*>(?<secnum>.*?)</span>')
    $secnum = ""
    if ($secnumMatch.Success) {
      $secnum = Convert-HtmlFragmentToPlainText -Html $secnumMatch.Groups["secnum"].Value
    }

    $title = $headingText
    if (-not [string]::IsNullOrWhiteSpace($secnum)) {
      $title = $title -replace ('^\s*' + [regex]::Escape($secnum) + '\s*'), ''
      $title = $title.Trim()
    }

    $map[$id] = [PSCustomObject]@{
      Id = $id
      Number = $secnum
      Title = $title
      Heading = $headingText
    }
  }

  return $map
}

function Get-SectionNumberDepth {
  param([string]$SectionNumber)

  if ([string]::IsNullOrWhiteSpace($SectionNumber)) {
    return -1
  }

  return ([regex]::Matches($SectionNumber, '\.')).Count
}

function Get-ChildSectionIndexMarkdown {
  param(
    [string]$HtmlPath,
    [string]$SectionId
  )

  $normalized = Normalize-SectionId -SectionId $SectionId
  $metadataMap = Get-SectionMetadataMapFromHtml -HtmlPath $HtmlPath
  if (-not $metadataMap.ContainsKey($normalized)) {
    return ""
  }

  $parent = $metadataMap[$normalized]
  if ([string]::IsNullOrWhiteSpace($parent.Number)) {
    return ""
  }

  $parentDepth = Get-SectionNumberDepth -SectionNumber $parent.Number
  $childIds = $metadataMap.Keys |
    Where-Object {
      $item = $metadataMap[$_]
      -not [string]::IsNullOrWhiteSpace($item.Number) -and
      $item.Number -like "$($parent.Number).*" -and
      (Get-SectionNumberDepth -SectionNumber $item.Number) -eq ($parentDepth + 1)
    } |
    Sort-Object {
      $metadataMap[$_].Number
    }

  if ($childIds.Count -eq 0) {
    return ""
  }

  $lines = @("## Child Sections", "")
  foreach ($childId in $childIds) {
    $item = $metadataMap[$childId]
    $display = if ([string]::IsNullOrWhiteSpace($item.Number)) { $item.Id } else { $item.Number }
    $lines += "- $display - ``$($item.Id)`` - $($item.Title)"
  }
  $lines += ""
  return ($lines -join "`r`n")
}

function Get-SectionHeadingFromHtml {
  param(
    [string]$HtmlPath,
    [string]$SectionId
  )

  $metadataMap = Get-SectionMetadataMapFromHtml -HtmlPath $HtmlPath
  $normalized = Normalize-SectionId -SectionId $SectionId
  if ($metadataMap.ContainsKey($normalized)) {
    return $metadataMap[$normalized].Heading
  }

  return $normalized
}

function Normalize-MarkdownText {
  param([string]$Markdown)

  if ([string]::IsNullOrWhiteSpace($Markdown)) {
    return $Markdown
  }

  $normalized = $Markdown
  $normalized = $normalized.Replace([char]0x00A0, ' ')
  $normalized = $normalized.Replace("ﾂ", " ")
  $normalized = $normalized.Replace("?ﾂ[", "? [")
  $normalized = $normalized -replace '[\u1680\u2000-\u200B\u202F\u205F\u3000\uFEFF]', ' '
  $normalized = $normalized -replace 'Â ', ' '
  $normalized = $normalized -replace '\?\s+\[', '? ['
  $normalized = $normalized -replace '!\s+\[', '! ['
  $normalized = $normalized -replace '\s+\)', ' )'
  $normalized = $normalized -replace '(?m)^# ([^\r\n]+?)  ', ('# $1' + "`r`n`r`n")
  $normalized = $normalized -replace '(\r?\n)?\s*(\d+\.)\s+', ("`r`n`r`n" + '$2 ')
  $normalized = $normalized -replace "`r?`n{3,}", "`r`n`r`n"
  return $normalized.TrimEnd()
}

function Remove-ChildSectionBlocks {
  param([string]$Markdown)

  if ([string]::IsNullOrWhiteSpace($Markdown)) {
    return $Markdown
  }

  return [regex]::Replace(
    $Markdown,
    '(?ms)^## Child Sections\s*\r?\n(?:\r?\n)?(?:- .*\r?\n)+(?:\r?\n)?',
    ''
  ).Trim()
}

function Remove-SourceLines {
  param([string]$Markdown)

  if ([string]::IsNullOrWhiteSpace($Markdown)) {
    return $Markdown
  }

  return [regex]::Replace(
    $Markdown,
    '(?m)^Source:\s+https?://\S+\s*$\r?\n?',
    ''
  ).Trim()
}

function Compose-SectionMarkdown {
  param(
    [string]$Markdown,
    [string]$SourceUrl,
    [string]$ChildSectionMarkdown,
    [string]$FallbackTitle
  )

  $body = Normalize-MarkdownText -Markdown $Markdown
  $childBlock = Normalize-MarkdownText -Markdown $ChildSectionMarkdown

  $heading = $null
  $bodyRemainder = $body
  $headingMatch = [regex]::Match($body, '^(?<heading># .+?)(?:\r?\n|$)(?<rest>[\s\S]*)$')
  if ($headingMatch.Success) {
    $heading = $headingMatch.Groups['heading'].Value.Trim()
    $bodyRemainder = $headingMatch.Groups['rest'].Value.Trim()
  } elseif (-not [string]::IsNullOrWhiteSpace($FallbackTitle)) {
    $heading = "# $FallbackTitle"
  }

  $bodyRemainder = Remove-ChildSectionBlocks -Markdown $bodyRemainder
  $bodyRemainder = Remove-SourceLines -Markdown $bodyRemainder
  $bodyRemainder = [regex]::Replace($bodyRemainder, '(?m)^- .*$', '').Trim()
  $bodyRemainder = [regex]::Replace($bodyRemainder, '\A(?:\s*-\s.*(?:\r?\n|$))+', '').Trim()

  $parts = @()
  if (-not [string]::IsNullOrWhiteSpace($heading)) {
    $parts += $heading
  }
  if (-not [string]::IsNullOrWhiteSpace($SourceUrl)) {
    $parts += "Source: $SourceUrl"
  }
  if (-not [string]::IsNullOrWhiteSpace($childBlock)) {
    $parts += $childBlock
  }
  if (-not [string]::IsNullOrWhiteSpace($bodyRemainder)) {
    $parts += $bodyRemainder
  }

  return (($parts -join "`r`n`r`n").TrimEnd())
}

function Repair-MarkdownFile {
  param(
    [string]$MarkdownPath,
    [string]$Url,
    [string]$PrefixMarkdown,
    [string]$SourceUrl,
    [string]$FallbackTitle
  )

  if (-not (Test-Path $MarkdownPath)) {
    return
  }

  $markdown = Get-Content -Raw -Path $MarkdownPath -ErrorAction SilentlyContinue
  if ([string]::IsNullOrWhiteSpace($markdown)) {
    return
  }

  $repaired = Compose-SectionMarkdown -Markdown $markdown -SourceUrl $SourceUrl -ChildSectionMarkdown $PrefixMarkdown -FallbackTitle $FallbackTitle

  if ($repaired -ne $markdown) {
    Set-Content -Path $MarkdownPath -Value $repaired -Encoding UTF8
  }
}

function Ensure-PageSectionIndex {
  param(
    [string]$PageName,
    [string]$MarkdownPageDir,
    [string]$CacheRoot,
    [string]$IndexUrl,
    [string]$OutDir,
    [switch]$ForceRefresh
  )

  $sectionsPath = Join-Path $MarkdownPageDir "sections.md"
  $existingSections = @()
  if (Test-Path $sectionsPath) {
    $existingSections = Get-Content -Path $sectionsPath -ErrorAction SilentlyContinue | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  }

  $indexDir = Join-Path $OutDir "index"
  $indexPageList = Join-Path $indexDir "ecma262-page-urls.txt"
  $sections = @()

  if (Test-Path $indexPageList) {
    $catalog = Get-CatalogData -IndexUrl $IndexUrl
    if ($catalog.SectionsByPage.ContainsKey($PageName)) {
      $sections = @($catalog.SectionsByPage[$PageName] | Sort-Object -Unique)
    }
  }

  $pageHtmlPath = Join-Path $CacheRoot $PageName "$PageName.html"
  if (Test-Path $pageHtmlPath) {
    $metadataMap = Get-SectionMetadataMapFromHtml -HtmlPath $pageHtmlPath
    if ($metadataMap.Count -gt 0) {
      $sections = foreach ($id in ($metadataMap.Keys | Sort-Object { $metadataMap[$_].Number }, { $_ })) {
        $item = $metadataMap[$id]
        if ([string]::IsNullOrWhiteSpace($item.Number)) {
          "$id - $($item.Title)"
        } elseif ([string]::IsNullOrWhiteSpace($item.Title)) {
          "$($item.Number) - $id"
        } else {
          "$($item.Number) - $id - $($item.Title)"
        }
      }
    } else {
      $sectionsFromHtml = Extract-SectionListFromHtml -HtmlPath $pageHtmlPath
      if ($sectionsFromHtml.Count -gt 0) {
        $sections = @($sections + $sectionsFromHtml | Sort-Object -Unique)
      }
    }
  }

  $finalSections = @($existingSections + $sections | Sort-Object -Unique)
  if ($finalSections.Count -eq 0) {
    if ($existingSections.Count -gt 0) {
      $finalSections = $existingSections
    }
  }

  Set-Content -Path $sectionsPath -Value ($finalSections | Sort-Object -Unique) -Encoding UTF8

  return $sectionsPath
}

function Build-MultipageCatalog {
  param(
    [string]$IndexUrl,
    [string]$OutDir
  )
  Save-CatalogArtifacts -OutDir $OutDir -IndexUrl $IndexUrl
}

function ConvertWithPandoc {
  param([string]$InputHtmlPath)

  $pandoc = Get-Command pandoc -ErrorAction SilentlyContinue
  if (-not $pandoc) {
    return $null
  }

  $out = [System.IO.Path]::GetTempFileName() + ".md"
  try {
    & $pandoc.Source --from=html --to=gfm --wrap=none --output $out $InputHtmlPath | Out-Null
    return Get-Content -Raw -Path $out
  } finally {
    Remove-Item $out -ErrorAction SilentlyContinue -Force
  }
}

function ConvertWithNode {
  param([string]$InputHtmlPath)

  $node = Get-Command node -ErrorAction SilentlyContinue
  if (-not $node) {
    return $null
  }

  $toolRoot = Split-Path -Parent $PSCommandPath
  $converterScript = Join-Path $toolRoot "ConvertHtmlToMarkdown.mjs"
  $out = & $node.Source $converterScript $InputHtmlPath $toolRoot 2>$null
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$out)) {
    return $null
  }
  return [string]$out
}

function ConvertWithBun {
  param([string]$InputHtmlPath)

  $bun = Get-Command bun -ErrorAction SilentlyContinue
  if (-not $bun) {
    return $null
  }

  $toolRoot = Split-Path -Parent $PSCommandPath
  $converterScript = Join-Path $toolRoot "ConvertHtmlToMarkdown.mjs"
  $out = & $bun.Source $converterScript $InputHtmlPath $toolRoot 2>$null
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$out)) {
    return $null
  }
  return [string]$out
}

function ConvertWithPython {
  param([string]$InputHtmlPath)

  $python = Get-Command python -ErrorAction SilentlyContinue
  if (-not $python) {
    return $null
  }

  $pyScript = @'
import pathlib
import sys

try:
    import html2text
except Exception:
    raise SystemExit(2)

html = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
print(html2text.html2text(html), end="")
'@

  $scriptPath = [System.IO.Path]::GetTempFileName() + ".py"
  try {
    Set-Content -Path $scriptPath -Value $pyScript -Encoding UTF8
    $out = & $python.Source $scriptPath $InputHtmlPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$out)) {
      return $null
    }
    return [string]$out
  } finally {
    Remove-Item $scriptPath -ErrorAction SilentlyContinue -Force
  }
}

function ConvertWithRemote {
  param([string]$Url)

  $url = if ($Url -match '^(https?):\/\/') {
    "https://r.jina.ai/$($Matches[1])://$($Url.Substring($Matches[0].Length))"
  } else {
    throw "Section url is not usable for remote conversion."
  }

  try {
    return (Invoke-WebRequest -Uri $url -UseBasicParsing -ErrorAction Stop).Content
  } catch {
    return $null
  }
}

function Convert-Html {
  param(
    [string]$InputHtmlPath,
    [string]$MarkdownPath,
    [string]$Url,
    [string]$PreferredConverter,
    [string]$InputHtml,
    [string]$PrefixMarkdown,
    [string]$SourceUrl,
    [string]$FallbackTitle
  )

  $tempInput = $null
  if (-not [string]::IsNullOrWhiteSpace($InputHtml)) {
    $tempInput = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $tempInput -Value $InputHtml -Encoding UTF8
    $InputHtmlPath = $tempInput
  }

  $hasSectionInput = -not [string]::IsNullOrWhiteSpace($InputHtml)

  $order = switch ($PreferredConverter) {
    "Pandoc" { @("Pandoc") }
    "Node" { @("Node") }
    "Bun" { @("Bun") }
    "Python" { @("Python") }
    "Remote" { @("Remote") }
    default {
      if ($hasSectionInput) {
        @("Pandoc", "Node", "Bun", "Python")
      } else {
        @("Pandoc", "Node", "Bun", "Python", "Remote")
      }
    }
  }

  if ($hasSectionInput -and $PreferredConverter -eq "Remote") {
    throw "Remote converter cannot be used for section-local extraction because it receives only full page URLs. Use a local converter (Pandoc/Node/Bun/Python) or remove '#section' from the request."
  }

  try {
    $markdown = $null
    foreach ($item in $order) {
      try {
        switch ($item) {
          "Pandoc" { $markdown = ConvertWithPandoc -InputHtmlPath $InputHtmlPath }
          "Node" { $markdown = ConvertWithNode -InputHtmlPath $InputHtmlPath }
          "Bun" { $markdown = ConvertWithBun -InputHtmlPath $InputHtmlPath }
          "Python" { $markdown = ConvertWithPython -InputHtmlPath $InputHtmlPath }
          "Remote" { $markdown = ConvertWithRemote -Url $Url }
        }
      } catch {
        Write-Verbose "Converter '$item' failed: $_"
        $markdown = $null
      }

      if (-not [string]::IsNullOrWhiteSpace($markdown)) {
        Write-Host "Converted with: $item"
        break
      }
    }

    if ([string]::IsNullOrWhiteSpace($markdown)) {
      if ($hasSectionInput) {
        throw @"
No section-scoped markdown converter was available.
Recommended setup:
  cd .\tools\OkojoEcma262
  npm install

This installs the JS converter dependency used for section extraction.
"@
      }

      throw @"
No markdown converter was available. Configure one of:
- node + turndown
- bun + turndown
- python + html2text
- pandoc
- fallback Remote converter (requires network access)
"@
    }

    $markdown = Compose-SectionMarkdown -Markdown $markdown -SourceUrl $SourceUrl -ChildSectionMarkdown $PrefixMarkdown -FallbackTitle $FallbackTitle
    if ($markdown -notmatch "(?m)^# ") {
      $markdown = "# $Url`r`n`r`n$markdown"
    }

    $outputDir = Split-Path -Parent $MarkdownPath
    if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
      New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    Set-Content -Path $MarkdownPath -Value $markdown -Encoding UTF8
  } finally {
    if ($tempInput) {
      Remove-Item $tempInput -ErrorAction SilentlyContinue -Force
    }
  }
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$cacheRoot = Join-Path $OutDir "cache"
$markdownRoot = Join-Path $OutDir "markdown"
  New-Item -ItemType Directory -Path $cacheRoot, $markdownRoot -Force | Out-Null
  if ($ShowHelp) {
    Show-ToolHelp
    return
  }

if ($Catalog) {
  Build-MultipageCatalog -IndexUrl $IndexUrl -OutDir $OutDir
  return
}

$indexDir = Join-Path $OutDir "index"
New-Item -ItemType Directory -Path $indexDir -Force | Out-Null
$indexPageList = Join-Path $indexDir "ecma262-page-urls.txt"

if (-not [string]::IsNullOrWhiteSpace($SectionUrl) -and -not $Section) {
  try {
    $sectionUrlUri = [uri]$SectionUrl
    $fragment = $sectionUrlUri.Fragment.TrimStart("#")
    if ($fragment -match "^sec-") {
      $Section = $fragment
    }
  } catch {
    # ignore malformed URL and keep existing argument validation
  }
}

$trimmedTarget = $Target.Trim()
if ($trimmedTarget -ieq "--help") {
  Show-ToolHelp
  return
}

if ([string]::IsNullOrWhiteSpace($trimmedTarget)) {
  if (Test-Path $indexPageList) {
    Get-Content -Path $indexPageList | ForEach-Object {
      if ($_ -match "/([^/]+)\.html$") {
        $matches[1]
      } else {
        $_
      }
    }
    return
  }

  (Get-CatalogData -IndexUrl $IndexUrl).PageUrls | ForEach-Object {
    if ($_ -match "/([^/]+)\.html$") {
      $matches[1]
    } else {
      $_
    }
  }
  return
}

if ($trimmedTarget -match '^([^#]+)#(.+)$') {
  $requestedPage = $matches[1]
  $requestedSection = $matches[2]
  $requestedPageName = if ($requestedPage -like "*.html") { [System.IO.Path]::GetFileNameWithoutExtension($requestedPage) } else { $requestedPage }
  $resolvedSection = Normalize-SectionId -SectionId $requestedSection
  $pageFile = if ($requestedPage -like "*.html") { $requestedPage } else { "$requestedPage.html" }
  $resolvedUrl = Get-AbsoluteUrl -BaseUrl $IndexUrl -RelativeUrl $pageFile
  $cacheDir = Join-Path $cacheRoot $requestedPageName
  $markdownDir = Join-Path $markdownRoot $requestedPageName
  $pageHtmlPath = Join-Path $cacheDir "$requestedPageName.html"
  $requestedContext = Get-SectionContext -Section $resolvedSection -ResolvedUri ([uri]"$resolvedUrl#$resolvedSection") -CacheDir $cacheRoot -MarkdownDir $markdownRoot
  New-Item -ItemType Directory -Path $cacheDir, $markdownDir -Force | Out-Null

  $downloaded = $false
  $pageCacheValid = Test-SectionHtmlCache -HtmlPath $pageHtmlPath
  if ($ForceRefresh -or -not $pageCacheValid) {
    Write-Host "Downloading $resolvedUrl"
    Invoke-WebRequest -Uri $resolvedUrl -UseBasicParsing -OutFile $pageHtmlPath -ErrorAction Stop
    $downloaded = $true
    $pageCacheValid = Test-SectionHtmlCache -HtmlPath $pageHtmlPath
  }

  if (-not $pageCacheValid) {
    throw "Downloaded page at '$resolvedUrl' was not a valid ECMA-262 HTML cache. Retry with -ForceRefresh."
  }

  $actualSectionId = Resolve-SectionIdFromHtml -HtmlPath $pageHtmlPath -RequestedSectionId $requestedSection
  $pageUrl = "$resolvedUrl#$actualSectionId"
  $context = Get-SectionContext -Section $actualSectionId -ResolvedUri ([uri]$pageUrl) -CacheDir $cacheRoot -MarkdownDir $markdownRoot
  $markdownPath = $context.MarkdownPath
  $sectionMarkdownPath = Join-Path $markdownDir "sections.md"
  if ($requestedSection -match '%') {
    $legacyEncodedAliasPath = Join-Path $markdownDir "sec-$(Get-SafePathPart $requestedSection).md"
    if ($legacyEncodedAliasPath -ne $markdownPath -and (Test-Path $legacyEncodedAliasPath)) {
      Remove-Item -LiteralPath $legacyEncodedAliasPath -Force -ErrorAction SilentlyContinue
    }
  }
  Remove-StaleSectionMarkdownAliases -SectionIds (Get-SectionIdCandidates -SectionId $requestedSection) -ActualSectionId $actualSectionId -ResolvedUri $pageUrl -CacheDir $cacheRoot -MarkdownDir $markdownRoot

  if ($ForceRefresh -or $downloaded -or -not (Test-Path $sectionMarkdownPath)) {
    Ensure-PageSectionIndex -PageName $requestedPageName -MarkdownPageDir $markdownDir -CacheRoot $cacheRoot -IndexUrl $IndexUrl -OutDir $OutDir -ForceRefresh:$ForceRefresh | Out-Null
  }

  if ($ForceRefresh -or -not (Test-Path $markdownPath) -or (Test-ScopedSectionMarkdown -MarkdownPath $markdownPath)) {
    $sectionHtml = Extract-SectionHtmlFromPage -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $childSectionIndex = Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $fallbackTitle = Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    if ([string]::IsNullOrWhiteSpace($sectionHtml)) {
      Write-Host "Could not isolate section '$($context.SectionId)' from cached HTML. Falling back to full-page conversion."
    }
    if ([string]::IsNullOrWhiteSpace($sectionHtml)) {
      Convert-Html -InputHtmlPath $pageHtmlPath -MarkdownPath $markdownPath -Url $resolvedUrl -PreferredConverter $PreferredConverter
    } else {
      Convert-Html -InputHtmlPath $pageHtmlPath -MarkdownPath $markdownPath -Url $resolvedUrl -PreferredConverter $PreferredConverter -InputHtml $sectionHtml -PrefixMarkdown $childSectionIndex -SourceUrl $pageUrl -FallbackTitle $fallbackTitle
    }
  } else {
    $childSectionIndex = Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $fallbackTitle = Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    Repair-MarkdownFile -MarkdownPath $markdownPath -Url $resolvedUrl -PrefixMarkdown $childSectionIndex -SourceUrl $pageUrl -FallbackTitle $fallbackTitle
  }

  Repair-MarkdownFile -MarkdownPath $markdownPath -Url $resolvedUrl -PrefixMarkdown (Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId) -SourceUrl $pageUrl -FallbackTitle (Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId)

  Write-Output $markdownPath
  if ($Open) {
    Invoke-Item $markdownPath
  }
  return
}

if ($trimmedTarget -match '^([^#\\\/]+)(?:\\.html)?$' -and -not ($trimmedTarget -like "sec-*")) {
  $requestedPage = $trimmedTarget
  if ($requestedPage -like "*.html") {
    $requestedPage = [System.IO.Path]::GetFileNameWithoutExtension($requestedPage)
  }
  $markdownPageDir = Join-Path $markdownRoot $requestedPage
  New-Item -ItemType Directory -Path $markdownPageDir -Force | Out-Null
  $sectionFile = Ensure-PageSectionIndex -PageName $requestedPage -MarkdownPageDir $markdownPageDir -CacheRoot $cacheRoot -IndexUrl $IndexUrl -OutDir $OutDir -ForceRefresh:$ForceRefresh
  if (Test-Path $sectionFile) {
    Get-Content -Path $sectionFile
  } else {
    Write-Host "No section inventory found for '$requestedPage'."
  }
  return
}

if (-not [string]::IsNullOrWhiteSpace($Section)) {
  $resolvedSection = Normalize-SectionId -SectionId $Section
  $resolvedUrl = Resolve-SectionUrl -SectionId $resolvedSection -SectionUrlInput $SectionUrl -IndexUrl $IndexUrl
  $resolvedUri = [uri]$resolvedUrl
  $requestedContext = Get-SectionContext -Section $resolvedSection -ResolvedUri $resolvedUri -CacheDir $cacheRoot -MarkdownDir $markdownRoot
  $context = Get-SectionContext -Section $resolvedSection -ResolvedUri $resolvedUri -CacheDir $cacheRoot -MarkdownDir $markdownRoot
  $cacheDir = $context.CachePageDir
  $markdownDir = $context.MarkdownPageDir
  $sectionMarkdownPath = Join-Path $markdownDir "sections.md"
  $markdownPath = $context.MarkdownPath

  $pageHtmlPath = $context.RawPagePath
  New-Item -ItemType Directory -Path $cacheDir, $markdownDir -Force | Out-Null

  $pageCacheValid = Test-SectionHtmlCache -HtmlPath $pageHtmlPath
  if ($ForceRefresh -or -not $pageCacheValid) {
    Write-Host "Downloading $resolvedUrl"
    Invoke-WebRequest -Uri $resolvedUrl -UseBasicParsing -OutFile $pageHtmlPath -ErrorAction Stop
    $pageCacheValid = Test-SectionHtmlCache -HtmlPath $pageHtmlPath
  }

  if (-not $pageCacheValid) {
    throw "Downloaded page at '$resolvedUrl' was not a valid ECMA-262 HTML cache. Retry with -ForceRefresh."
  }

  $actualSectionId = Resolve-SectionIdFromHtml -HtmlPath $pageHtmlPath -RequestedSectionId $resolvedSection
  if ($actualSectionId -ne $context.SectionId) {
    $resolvedUri = [uri]"$($resolvedUri.GetLeftPart([System.UriPartial]::Path))#$actualSectionId"
    $context = Get-SectionContext -Section $actualSectionId -ResolvedUri $resolvedUri -CacheDir $cacheRoot -MarkdownDir $markdownRoot
    $cacheDir = $context.CachePageDir
    $markdownDir = $context.MarkdownPageDir
    $sectionMarkdownPath = Join-Path $markdownDir "sections.md"
    $markdownPath = $context.MarkdownPath
  }
  Remove-StaleSectionMarkdownAliases -SectionIds (Get-SectionIdCandidates -SectionId $Section) -ActualSectionId $actualSectionId -ResolvedUri $resolvedUri.AbsoluteUri -CacheDir $cacheRoot -MarkdownDir $markdownRoot

  if ($ForceRefresh -or -not (Test-Path $sectionMarkdownPath)) {
    Ensure-PageSectionIndex -PageName $context.PageName -MarkdownPageDir $markdownDir -CacheRoot $cacheRoot -IndexUrl $IndexUrl -OutDir $OutDir -ForceRefresh:$ForceRefresh | Out-Null
  }

  if ($ForceRefresh -or -not (Test-Path $markdownPath) -or (Test-ScopedSectionMarkdown -MarkdownPath $markdownPath)) {
    $sectionHtml = Extract-SectionHtmlFromPage -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $childSectionIndex = Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $fallbackTitle = Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    if ([string]::IsNullOrWhiteSpace($sectionHtml)) {
      Write-Host "Could not isolate section '$($context.SectionId)' from cached HTML. Falling back to full-page conversion."
    }
    if ([string]::IsNullOrWhiteSpace($sectionHtml)) {
      Convert-Html -InputHtmlPath $pageHtmlPath -MarkdownPath $markdownPath -Url $resolvedUrl -PreferredConverter $PreferredConverter
    } else {
      Convert-Html -InputHtmlPath $pageHtmlPath -MarkdownPath $markdownPath -Url $resolvedUrl -PreferredConverter $PreferredConverter -InputHtml $sectionHtml -PrefixMarkdown $childSectionIndex -SourceUrl $resolvedUrl -FallbackTitle $fallbackTitle
    }
  } else {
    $childSectionIndex = Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    $fallbackTitle = Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId
    Repair-MarkdownFile -MarkdownPath $markdownPath -Url $resolvedUrl -PrefixMarkdown $childSectionIndex -SourceUrl $resolvedUrl -FallbackTitle $fallbackTitle
  }

  Repair-MarkdownFile -MarkdownPath $markdownPath -Url $resolvedUrl -PrefixMarkdown (Get-ChildSectionIndexMarkdown -HtmlPath $pageHtmlPath -SectionId $context.SectionId) -SourceUrl $resolvedUrl -FallbackTitle (Get-SectionHeadingFromHtml -HtmlPath $pageHtmlPath -SectionId $context.SectionId)

  Write-Output $markdownPath
  if ($Open) {
    Invoke-Item $markdownPath
  }
  return
}

if ([string]::IsNullOrWhiteSpace($Section) -and [string]::IsNullOrWhiteSpace($SectionUrl)) {
  throw "Could not parse target '$Target'. Use -h/--help for usage."
}

throw "Provide a target or use -Section."
