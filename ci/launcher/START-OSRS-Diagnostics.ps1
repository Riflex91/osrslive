# Windows PowerShell 5.1 launcher. Writes diagnostics before any Java call.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location -LiteralPath $root
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'startup.log'
function Write-Start([string]$phase, [string]$detail) {
    $line = '>' + (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss.fff') + " [$phase] $detail"
    Add-Content -LiteralPath $log -Value $line -Encoding UTF8
    Write-Host $line
}
try {
    Write-Start 'INIT' "Arbeitsordner=$root | PowerShell=$($PSVersionTable.PSVersion)"
    $jar = Join-Path $root 'OSRS-Diagnostics-Client.jar'
    $finder = Join-Path $root 'FIND-JAVA.ps1'
    $updater = Join-Path $root 'UPDATE-OSRS-Diagnostics.ps1'
    if (-not (Test-Path -LiteralPath $jar -PathType Leaf)) { throw 'JAR fehlt. Bitte ZIP komplett entpacken.' }
    if (-not (Test-Path -LiteralPath $finder -PathType Leaf)) { throw 'FIND-JAVA.ps1 fehlt.' }
    Write-Start 'JAVA_DISCOVERY_START' 'Pruefe Java-Laufzeiten'
    $result = @(& $finder)
    $java = @($result | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1)
    if ($java.Count -eq 0) { throw 'Keine Java-17+-Laufzeit gefunden. Siehe logs/java-discovery.log' }
    $javaExe = [string]$java[0]
    $env:OSRS_JAVA_EXE = $javaExe
    Write-Start 'JAVA_DISCOVERY_OK' "java=$javaExe"
    Write-Start 'SELFTEST_START' 'Selbsttest des installierten Clients'
    $self = & $javaExe '-Djava.awt.headless=true' '-jar' $jar '--self-test' 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Selbsttest fehlgeschlagen: $self" }
    Write-Start 'SELFTEST_OK' ($self.Trim() -replace "[\r\n]+", ' | ')
    if (Test-Path -LiteralPath $updater -PathType Leaf) {
        Write-Start 'UPDATE_START' 'GitHub-Updatepruefung (Offline-Start bei Fehler)'
        try {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $updater -JavaExecutable $javaExe | Out-Null
            if ($LASTEXITCODE -eq 0) { Write-Start 'UPDATE_OK' 'Updatepruefung abgeschlossen' }
            else { Write-Start 'UPDATE_UNAVAILABLE' "Exitcode=$LASTEXITCODE | lokale JAR" }
        } catch { Write-Start 'UPDATE_UNAVAILABLE' "Fehler=$($_.Exception.GetType().Name) | lokale JAR" }
    }
    Write-Start 'GUI_START' 'Starte Swing; logs/startup-java.log'
    $stdout = Join-Path $logDir 'java-stdout.log'
    $stderr = Join-Path $logDir 'java-stderr.log'
    $process = Start-Process -FilePath $javaExe -ArgumentList @('-jar','"OSRS-Diagnostics-Client.jar"') -WorkingDirectory $root -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -Wait -NoNewWindow
    Write-Start 'GUI_EXIT' "Exitcode=$($process.ExitCode)"
    if ($process.ExitCode -ne 0) { throw "GUI-Prozess mit Code $($process.ExitCode) beendet. logs/java-stderr.log lesen." }
    exit 0
} catch {
    Write-Start 'START_FAILED' "$($_.Exception.GetType().Name): $($_.Exception.Message)"
    exit 1
}
