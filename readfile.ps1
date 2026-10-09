$fp=$args[0]
$raw=[System.IO.File]::ReadAllBytes($fp)
$n=$raw.Count
Write-Output('len:' + $n)
Write-Output('LF:' + ($raw | measure -Sum { if($_ -eq 10) { $_ } } | select -ExpandProperty Sum))
Write-Output('sample hex[0..11]=' + ([BitConverter]::ToString($raw[0..11])))
