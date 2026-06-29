<#
  run.ps1 - build the Prospector's Dispatch mod and launch Vintage Story with it loaded.

  Usage (from anywhere):
    .\run.ps1                  # build, then launch to the main menu (click your world)
    .\run.ps1 -NoBuild         # skip the build, just launch
    .\run.ps1 -World pdtest    # also auto-open the 'pdtest' world (creates it if missing)
    .\run.ps1 -World pdtest -PlayStyle preset-surviveandbuild

  Notes:
    - Close the game before re-running: it locks the mod DLL while running, so a rebuild will fail.
    - Assets (itemtypes/lang/textures) are bundled into the build output, so only --addModPath is needed.
    - -World requires a valid -PlayStyle langcode (the game's arg parser needs one even to reopen a world).
      Default preset-surviveandbuild generates NORMAL terrain (has ore maps). Avoid preset-creativebuilding
      for testing this mod: it's a SUPERFLAT world with no ore generation, so no dispatches will appear.
      Other langcodes: preset-exploration / preset-wildernesssurvival. Use /gamemode creative once in-world.
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$World,
    [string]$PlayStyle = "preset-surviveandbuild"
)

$ErrorActionPreference = "Stop"

# Resolve VINTAGE_STORY even from a shell opened before the env var was set.
if (-not $env:VINTAGE_STORY) {
    $env:VINTAGE_STORY = [Environment]::GetEnvironmentVariable("VINTAGE_STORY", "User")
}
if (-not $env:VINTAGE_STORY) {
    throw "VINTAGE_STORY is not set. Point it at your Vintage Story install directory."
}

$proj = Join-Path $PSScriptRoot "ProspectorsDispatch"
$exe  = Join-Path $env:VINTAGE_STORY "Vintagestory.exe"

if (-not $NoBuild) {
    Write-Host "Building..." -ForegroundColor Cyan
    dotnet build (Join-Path $proj "ProspectorsDispatch.csproj") -c Debug --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed (is the game still running and locking the DLL?)."
    }
}

$gameArgs = @("--addModPath", (Join-Path $proj "bin\Debug\Mods"))
if ($World) {
    $gameArgs += @("--openWorld", $World, "--playStyle", $PlayStyle)
    Write-Host "Launching world '$World'..." -ForegroundColor Green
}
else {
    Write-Host "Launching to main menu..." -ForegroundColor Green
}

& $exe @gameArgs
