#!/usr/bin/env pwsh

# Script para marcar interfaces e classes como [Obsolete]
# Execução: .\mark-obsolete.ps1

$repoPath = "C:\Users\herna\source\repos\DnsBlocker"
$basePath = Join-Path $repoPath "src\NextDnsBetBlocker.Core"

function Mark-Obsolete-Interface {
    param(
        [string]$FilePath,
        [string]$InterfaceName,
        [string]$ObsoleteMessage
    )
    
    $content = Get-Content $FilePath -Raw
    
    # Padrão para encontrar a interface pública
    $pattern = "public interface $InterfaceName"
    
    # Verifica se já está marcado como obsolete
    if ($content -like "*[Obsolete*$InterfaceName*") {
        Write-Host "✓ $InterfaceName já está marcado como [Obsolete]" -ForegroundColor Yellow
        return $true
    }
    
    # Encontra o comentário resumido antes da interface e adiciona [Obsolete]
    $newContent = $content -replace `
        "(/// </summary>\s*\n)(public interface $InterfaceName)", `
        "`$1[Obsolete(""$ObsoleteMessage"", false)]`n`$2"
    
    if ($newContent -ne $content) {
        Set-Content $FilePath $newContent -Encoding UTF8 -NoNewline
        Write-Host "✓ Marcado $InterfaceName como [Obsolete]" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "✗ Falha ao marcar $InterfaceName" -ForegroundColor Red
        return $false
    }
}

function Mark-Obsolete-Class {
    param(
        [string]$FilePath,
        [string]$ClassName,
        [string]$ObsoleteMessage
    )
    
    $content = Get-Content $FilePath -Raw
    
    # Padrão para encontrar a classe pública
    $pattern = "public class $ClassName"
    
    # Verifica se já está marcado como obsolete
    if ($content -like "*[Obsolete*$ClassName*") {
        Write-Host "✓ $ClassName já está marcado como [Obsolete]" -ForegroundColor Yellow
        return $true
    }
    
    # Encontra o comentário e adiciona [Obsolete]
    $newContent = $content -replace `
        "(/// </summary>\s*\n)(public class $ClassName)", `
        "`$1[Obsolete(""$ObsoleteMessage"", false)]`n`$2"
    
    if ($newContent -ne $content) {
        Set-Content $FilePath $newContent -Encoding UTF8 -NoNewline
        Write-Host "✓ Marcado $ClassName como [Obsolete]" -ForegroundColor Green
        return $true
    }
    else {
        Write-Host "✗ Falha ao marcar $ClassName" -ForegroundColor Red
        return $false
    }
}

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Marcando componentes não utilizados como [Obsolete]" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# === INTERFACES ===
Write-Host "Marcando INTERFACES..." -ForegroundColor Magenta
Write-Host ""

Mark-Obsolete-Interface `
    -FilePath "$basePath\Interfaces\Interfaces.cs" `
    -InterfaceName "INextDnsClient" `
    -ObsoleteMessage "This interface is not used in the current implementation. Use ILogsProducer instead."

Mark-Obsolete-Interface `
    -FilePath "$basePath\Interfaces\Interfaces.cs" `
    -InterfaceName "ICheckpointStore" `
    -ObsoleteMessage "This interface is not used in the current implementation."

Mark-Obsolete-Interface `
    -FilePath "$basePath\Interfaces\Interfaces.cs" `
    -InterfaceName "IBlockedDomainStore" `
    -ObsoleteMessage "This interface is not used in the current implementation."

Mark-Obsolete-Interface `
    -FilePath "$basePath\Interfaces\Interfaces.cs" `
    -InterfaceName "IGamblingSuspectAnalyzer" `
    -ObsoleteMessage "This interface is not used in the current implementation."

Write-Host ""
Write-Host "Marcando CLASSES..." -ForegroundColor Magenta
Write-Host ""

Mark-Obsolete-Class `
    -FilePath "$basePath\Services\NextDnsClient.cs" `
    -ClassName "NextDnsClient" `
    -ObsoleteMessage "This class is not used in the current implementation. Use LogsProducer instead."

Mark-Obsolete-Class `
    -FilePath "$basePath\Services\BlockedDomainStore.cs" `
    -ClassName "BlockedDomainStore" `
    -ObsoleteMessage "This class is not used in the current implementation."

Mark-Obsolete-Class `
    -FilePath "$basePath\Services\CheckpointStore.cs" `
    -ClassName "CheckpointStore" `
    -ObsoleteMessage "This class is not used in the current implementation."

Mark-Obsolete-Class `
    -FilePath "$basePath\Services\GamblingSuspectAnalyzer.cs" `
    -ClassName "GamblingSuspectAnalyzer" `
    -ObsoleteMessage "This class is not used in the current implementation."

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  ✓ Processo concluído!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
