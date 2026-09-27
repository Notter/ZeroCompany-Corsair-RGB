param([string]$SdkPath)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Install the .NET SDK first.' }
$sdkLine = & dotnet --list-sdks | Where-Object { $_ -like "$sdkVersion *" } | Select-Object -First 1
if ($sdkLine -notmatch '\[(.+)\]') { throw 'Cannot locate the selected .NET SDK.' }
$compiler = Join-Path $Matches[1] "$sdkVersion\Roslyn\bincore\csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('mscorlib', 'System', 'System.Core') | ForEach-Object { '/reference:"' + (Join-Path $framework "$_.dll") + '"' }
$out = Join-Path $PSScriptRoot 'dist\ZeroCompanyRGB'
New-Item -ItemType Directory -Force $out | Out-Null
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:winexe /platform:x64 /optimize+ "/out:$out\ZeroCompanyRGB.exe" $refs 'src\Native.cs' 'src\State.cs' 'src\Renderer.cs' 'src\Program.cs'
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item 'mod\*' $out -Recurse -Force
if ($SdkPath) {
    $dll = Join-Path $SdkPath 'redist\x64\iCUESDK.x64_2019.dll'
    if (!(Test-Path -LiteralPath $dll)) { throw 'SdkPath must point to the extracted iCUESDK directory.' }
    Copy-Item -LiteralPath $dll -Destination $out -Force
}
Write-Output "Built: $out"
if (!$SdkPath) { Write-Output 'Compilation complete; the Corsair runtime DLL is needed only to run the helper.' }
