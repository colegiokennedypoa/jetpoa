<#
  Atualiza o serviço com a versão atual do código (PowerShell COMO ADMINISTRADOR).
  Faz backup da versão anterior em <Pasta>.anterior para voltar rápido se precisar.
  Voltar versão:  Stop-Service JetPOABistro; Remove-Item C:\Projeto\JetPOA\app -Recurse;
                  Rename-Item C:\Projeto\JetPOA\app.anterior app; Start-Service JetPOABistro
#>
param([string]$Pasta = 'C:\Projeto\JetPOA\app')
$ErrorActionPreference = 'Stop'

$temp = "$Pasta.nova"
& (Join-Path $PSScriptRoot 'publicar.ps1') -Destino $temp   # compila e testa ANTES de parar o serviço

Stop-Service JetPOABistro
if (Test-Path "$Pasta.anterior") { Remove-Item "$Pasta.anterior" -Recurse -Force }
if (Test-Path $Pasta) { Rename-Item $Pasta (Split-Path "$Pasta.anterior" -Leaf) }
Rename-Item $temp (Split-Path $Pasta -Leaf)
Start-Service JetPOABistro

Start-Sleep 5
$h = Invoke-RestMethod 'http://127.0.0.1:5080/api/health' -TimeoutSec 10
Write-Host "Atualizado · Oracle $($h.banco)" -ForegroundColor Green
