param(
    [string]$UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor',
    [string]$VisualStudioPath = 'C:\Program Files\Microsoft Visual Studio\2022\Community'
)
$ErrorActionPreference = 'Stop'
$routerProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$routerToolset = Get-ChildItem -LiteralPath (Join-Path $VisualStudioPath 'VC\Tools\MSVC') -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $routerToolset) { throw 'An MSVC C++ toolset is required.' }
$routerCompiler = Join-Path $routerToolset.FullName 'bin\Hostx64\x64\cl.exe'
$routerLinker = Join-Path $routerToolset.FullName 'bin\Hostx64\x64\link.exe'
$routerHeaders = Join-Path $UnityEditorPath 'Data\PluginAPI'
$routerBuild = Join-Path $routerProjectRoot 'Temp\CafeAudioRouterBuild'
$routerOutput = Join-Path $routerProjectRoot 'Assets\Plugins\x86_64'
New-Item -ItemType Directory -Path $routerBuild, $routerOutput -Force | Out-Null
$routerObject = Join-Path $routerBuild 'CafeAudioRouter.obj'
$routerDll = Join-Path $routerBuild 'CafeAudioRouter.dll'
& $routerCompiler /nologo /utf-8 /c /O2 /GS- /GR- /Zl "/I$routerHeaders" "/Fo$routerObject" (Join-Path $PSScriptRoot 'CafeAudioRouter.cpp')
if ($LASTEXITCODE -ne 0) { throw 'Audio router compilation failed.' }
& $routerLinker /nologo /dll /noentry /nodefaultlib "/out:$routerDll" "/implib:$(Join-Path $routerBuild 'CafeAudioRouter.lib')" $routerObject
if ($LASTEXITCODE -ne 0) { throw 'Audio router linking failed.' }
Copy-Item -LiteralPath $routerDll -Destination (Join-Path $routerOutput 'CafeAudioRouter.dll') -Force
Write-Output 'Built CafeAudioRouter.dll. Open Unity to import it.'
