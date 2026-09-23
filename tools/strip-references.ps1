<#
.SYNOPSIS
    Writes metadata-only copies of the assemblies Keepsake compiles against into lib/.

.DESCRIPTION
    A build server has no Valheim install, and the game's assemblies are not ours to publish.
    What a compiler actually needs is the shape of the types (names, signatures, inheritance)
    and none of the code inside them. So each method body here is replaced with `throw null`,
    embedded resources are dropped and field data is emptied, leaving a DLL that can be
    referenced but not run, and that carries none of the game's implementation.

    Jotunn and BepInEx are redistributable and could be downloaded at build time instead, but
    they are stripped alongside the rest so that CI has exactly one mechanism to trust.

    Mono.Cecil does the work and ships inside BepInEx, so there is nothing to install.

    Run this again after a game update, after a Jotunn update, or after changing which
    assemblies the project references, and commit the result.

.EXAMPLE
    pwsh tools/strip-references.ps1
    pwsh tools/strip-references.ps1 -ValheimDir "D:\Games\Valheim" -Out lib
#>

[CmdletBinding()]
param(
    # VALHEIM_DIR if it is set, a stock Steam install otherwise. -ValheimDir beats both.
    [string]$ValheimDir = $(if ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Valheim" }),
    [string]$BepInExDir = "$env:APPDATA\com.kesomannen.gale\valheim\profiles\Default\BepInEx",
    [string]$Out
)

$ErrorActionPreference = "Stop"

if (-not $Out) {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Out = Join-Path $root "..\lib"
}

$managed = Join-Path $ValheimDir "valheim_Data\Managed"
$core = Join-Path $BepInExDir "core"
$jotunn = Join-Path $BepInExDir "plugins\ValheimModding-Jotunn"
$cecil = Join-Path $core "Mono.Cecil.dll"

foreach ($path in @($managed, $core, $jotunn, $cecil)) {
    if (-not (Test-Path $path)) { throw "Not found: $path" }
}

# Every assembly listed in src/Keepsake/Keepsake.csproj and src/Keepsake.Preloader/Keepsake.Preloader.csproj.
# Keep the lists in step.
$sources = @(
    (Join-Path $core "BepInEx.dll"),
    (Join-Path $core "0Harmony.dll"),
    (Join-Path $core "Mono.Cecil.dll"),
    (Join-Path $jotunn "Jotunn.dll"),
    (Join-Path $managed "assembly_valheim.dll"),
    (Join-Path $managed "assembly_utils.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.AudioModule.dll"),
    (Join-Path $managed "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $managed "UnityEngine.UIModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managed "Unity.TextMeshPro.dll")
)

Add-Type -Path $cecil

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory($core)
$resolver.AddSearchDirectory($jotunn)

$parameters = New-Object Mono.Cecil.ReaderParameters
$parameters.AssemblyResolver = $resolver
$parameters.ReadingMode = [Mono.Cecil.ReadingMode]::Immediate

New-Item -ItemType Directory -Force -Path $Out | Out-Null

function Strip-Type {
    param($Type)

    foreach ($method in $Type.Methods) {
        if (-not $method.HasBody) { continue }

        $body = New-Object Mono.Cecil.Cil.MethodBody $method
        $il = $body.GetILProcessor()
        # `throw null` satisfies every return type without knowing what it is.
        $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldnull))
        $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Throw))
        $method.Body = $body
    }

    # Static arrays and the like keep their bytes in the PE file; the shape does not need them.
    foreach ($field in $Type.Fields) {
        if ($field.InitialValue -and $field.InitialValue.Length -gt 0) {
            $field.InitialValue = New-Object byte[] 0
        }
    }

    foreach ($nested in $Type.NestedTypes) { Strip-Type -Type $nested }
}

$total = 0
foreach ($source in $sources) {
    if (-not (Test-Path $source)) { throw "Not found: $source" }

    $name = Split-Path $source -Leaf
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($source, $parameters)
    try {
        foreach ($module in $assembly.Modules) {
            foreach ($type in $module.Types) { Strip-Type -Type $type }
            $module.Resources.Clear()
        }

        $target = Join-Path $Out $name
        $assembly.Write($target)

        $before = (Get-Item $source).Length / 1KB
        $after = (Get-Item $target).Length / 1KB
        $total += $after
        "{0,-38} {1,8:N0} KB -> {2,7:N0} KB" -f $name, $before, $after
    }
    finally {
        $assembly.Dispose()
    }
}

""
"{0} assemblies, {1:N0} KB in {2}" -f $sources.Count, $total, (Resolve-Path $Out)
