$ErrorActionPreference = 'Stop'
$src  = 'C:\Users\lenovo\Desktop\数位板工程\publish\win-x64'
$out  = 'C:\Users\lenovo\Desktop\数位板工程\installer\files.wxi'

$map = @{
    'models\det' = 'd_models_det'
    'models\rec' = 'd_models_rec'
}

function Sanitize([string]$s){
    $r = $s -replace '[\/]', '__'
    $r = $r -replace '[^A-Za-z0-9_]', '_'
    return $r
}

$sb = New-Object System.Text.StringBuilder
$null = $sb.AppendLine('<Fragment>')
$null = $sb.AppendLine('  <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">')

$files = Get-ChildItem -Path $src -Recurse -File | Sort-Object FullName
foreach($f in $files){
    $rel = $f.FullName.Substring($src.Length + 1).Replace('\', '\')
    if($rel -eq 'HandwriteInput.PoC.exe'){ continue }

    $dir = Split-Path -Parent $rel
    $dirid = 'INSTALLFOLDER'
    if($map.ContainsKey($dir)){ $dirid = $map[$dir] }

    $cid = 'c_' + (Sanitize $rel)
    $fid = 'f_' + (Sanitize $rel)
    $srcPath = $f.FullName

    $null = $sb.AppendLine('    <Component Id="' + $cid + '" Guid="*" Directory="' + $dirid + '">')
    $null = $sb.AppendLine('      <File Id="' + $fid + '" Source="' + $srcPath + '" KeyPath="yes" />')
    $null = $sb.AppendLine('    </Component>')
}

$null = $sb.AppendLine('  </ComponentGroup>')
$null = $sb.AppendLine('</Fragment>')

[System.IO.File]::WriteAllText($out, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output ("Generated " + $files.Count + " files; components=" + ([regex]::Matches($sb.ToString(), '<Component ')).Count)
