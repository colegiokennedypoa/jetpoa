<#
  Publica o JetPOA Bistrô (front + API) numa pasta de execução.
  Uso:  .\scripts\publicar.ps1                       (padrão: C:\Projeto\JetPOA\app)
        .\scripts\publicar.ps1 -Destino D:\JetPOA\app
  Não mexe no serviço: para atualizar um serviço em execução use atualizar.ps1.
#>
param([string]$Destino = 'C:\Projeto\JetPOA\app')
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent

Write-Host '1/3 Front-end (React)...'
Push-Location (Join-Path $raiz 'web')
try {
    if (-not (Test-Path node_modules)) { npm ci --no-fund --no-audit }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Falha no build do front-end.' }
} finally { Pop-Location }

Write-Host '2/3 Testes...'
dotnet test (Join-Path $raiz 'JetPoa.sln') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Testes falharam: publicação cancelada.' }

Write-Host "3/3 Publicando API em ${Destino} ..."
dotnet publish (Join-Path $raiz 'src\JetPoa.Api\JetPoa.Api.csproj') -c Release -o $Destino -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falha no publish.' }

Write-Host "Publicado em $Destino" -ForegroundColor Green
Write-Host 'A configuração (banco, certificado, CSC) continua em C:\Projeto\certificado\52822987000123.conf'
