$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$sdkVersion = & dotnet --version
$compiler = Join-Path 'C:\Program Files\dotnet\sdk' "$sdkVersion\Roslyn\bincore\csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('mscorlib', 'System', 'System.Core') | ForEach-Object { '/reference:"' + (Join-Path $framework "$_.dll") + '"' }
& dotnet $compiler /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /optimize+ '/out:tests\Tests.exe' $refs 'src\Native.cs' 'src\State.cs' 'src\Renderer.cs' 'tests\Tests.cs'
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& '.\tests\Tests.exe'
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
