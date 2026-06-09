$root = 'D:\DeepSeek CLI\DeepSeekCode'
$dirs = Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -notin @('bin','obj') }
foreach ($d in $dirs) {
    Write-Output "=== $($d.Name) ==="
    $items = Get-ChildItem -Name $d.FullName
    Write-Output ($items -join ', ')
    Write-Output ""
}
