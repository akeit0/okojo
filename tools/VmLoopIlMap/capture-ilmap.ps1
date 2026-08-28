param(
    [Parameter(Mandatory = $true)][string[]]$Cases,
    [int]$Iterations = 1,
    [int]$Warmup = 400,
    [string]$Env = "tiered-off",
    [string]$OutDir = "$env:TEMP\opencode"
)

$ErrorActionPreference = "Stop"

$Cases = $Cases | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() }

foreach ($Case in $Cases)
{
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = "dotnet"
    $psi.Arguments = "tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll $Case $Iterations $Warmup --hold"
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.UseShellExecute = $false
    if ($Env -eq "tiered-off")
    {
        $psi.EnvironmentVariables["DOTNET_TieredCompilation"] = "0"
        $psi.EnvironmentVariables["DOTNET_TieredPGO"] = "0"
    }
    elseif ($Env -eq "pgo-off")
    {
        $psi.EnvironmentVariables["DOTNET_TieredPGO"] = "0"
    }

    $p = [System.Diagnostics.Process]::Start($psi)
    try
    {
        $pid2 = $null
        while (-not $p.StandardOutput.EndOfStream)
        {
            $line = $p.StandardOutput.ReadLine()
            if ($line -match '\[hold\] pid=(\d+)')
            {
                $pid2 = $Matches[1]
                break
            }
        }

        if ($null -eq $pid2)
        {
            Write-Output "[skip] $Case : probe did not reach the hold point"
            continue
        }

        Write-Output "[hold] pid=$pid2 case=$Case env=$Env"
        $out = Join-Path $OutDir "ilmap-$Case-$Env.txt"
        dotnet tools/VmLoopIlMap/bin/Release/net10.0/VmLoopIlMap.dll $pid2 --output $out
        if ($LASTEXITCODE -ne 0)
        {
            throw "VmLoopIlMap failed for pid $pid2."
        }

        $native = Select-String -Path $out -Pattern '\[native\]'
        $tier = Select-String -Path $out -Pattern 'compilation=(\w+)'
        Write-Output "[done] $Case :: $($tier.Matches[0].Groups[1].Value) :: $($native.Line)"
    }
    finally
    {
        if (-not $p.HasExited)
        {
            $p.Kill()
        }
        $p.Dispose()
    }
}
