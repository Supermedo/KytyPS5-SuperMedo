# Packs `precompile.py seeds` as kyty_seeds.exe (PyInstaller, one file), so a release can make the seed
# file from the game files without Python: precompile-windows.ps1 and the Kyty Launcher run it when the
# seed file is missing. Needs Python 3 with pip; the packages go into _Build\seedsvenv.
#   .\tools\local\static-precompile\build-seeds-exe.ps1      -> _Build\seeds-dist\kyty_seeds.exe
$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path "$PSScriptRoot\..\..\.."
$build = "$repo\_Build"
if (!(Test-Path "$build\seedsvenv\Scripts\python.exe")) {
	python -m venv "$build\seedsvenv"
	& "$build\seedsvenv\Scripts\python.exe" -m pip install --quiet pyinstaller numpy
}
# warmfile.py reads these two headers (enum names) relative to the bundle when frozen.
$data = @(
	"$PSScriptRoot\pass-states.json;.",
	"$repo\3rdparty\Vulkan-Headers\include\vulkan\vulkan_core.h;3rdparty\Vulkan-Headers\include\vulkan",
	"$repo\src\graphics\guest_gpu\gpu_defs.h;src\graphics\guest_gpu"
)
$arguments = @('--onefile', '--name', 'kyty_seeds', '--distpath', "$build\seeds-dist", '--workpath', "$build\seeds-work",
               '--specpath', "$build\seeds-work", '--paths', $PSScriptRoot, '--exclude-module', 'tkinter', '--noconfirm')
foreach ($item in $data) { $arguments += @('--add-data', $item) }
# PyInstaller logs to stderr, which Windows PowerShell turns into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
& "$build\seedsvenv\Scripts\python.exe" -m PyInstaller --log-level WARN @arguments "$PSScriptRoot\precompile.py" 2>&1 | Out-Host
if ($LASTEXITCODE) { throw 'PyInstaller failed' }
