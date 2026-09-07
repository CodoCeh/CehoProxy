param(
    [string]$Source = "",
    [string]$Repo = "CodoCeh/CehoProxy"
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host "Нужны права администратора: откройте PowerShell от имени администратора и повторите."
    exit 1
}

$root = Join-Path $env:ProgramData 'CehoProxy'
$exe  = Join-Path $root 'cehoproxy.exe'
$engine = Join-Path $root 'sing-box.exe'

$running = Get-Process -Name 'cehoproxy' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Останавливаю работающий CehoProxy перед обновлением..."
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
}

if (-not $Source) {
    $arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
    if ($arch -ne 'x64') { Write-Host "Поддерживается только 64-разрядная Windows."; exit 1 }

    $url = "https://github.com/$Repo/releases/latest/download/cehoproxy-win-x64.exe"
    $tmp = Join-Path $env:TEMP 'cehoproxy-download.exe'
    Write-Host "Скачиваю программу из релизов $Repo…"
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    } catch {
        Write-Host ""
        Write-Host "Скачать не удалось: $url"
        Write-Host "Так бывает, если релизов ещё нет или репозиторий закрыт."
        Write-Host "Тогда соберите программу сами и повторите с путём к файлу:"
        Write-Host "  .\install.ps1 -Source .\cehoproxy.exe"
        exit 1
    }
    $Source = $tmp
}

if (-not (Test-Path $Source)) { Write-Host "Не найден файл программы: $Source"; exit 1 }

New-Item -ItemType Directory -Force -Path $root | Out-Null
Copy-Item -Path $Source -Destination $exe -Force

# Установка / обновление дочерних процессов (sing-box)
if ($Source) {
    $sourceDir = Split-Path -Parent (Resolve-Path $Source)
    $nearbyEngine = Join-Path $sourceDir 'sing-box.exe'
    if (Test-Path $nearbyEngine) {
        Copy-Item -Path $nearbyEngine -Destination $engine -Force
        Write-Host "Движок sing-box установлен из локального источника: $engine"
    }
}

if (-not (Test-Path $engine) -and -not (Get-Command 'sing-box' -ErrorAction SilentlyContinue)) {
    Write-Host "Движок sing-box не найден. Загружаю sing-box для Windows x64..."
    $installedEngine = $false

    try {
        $chpSbUrl = "https://github.com/$Repo/releases/latest/download/sing-box.exe"
        Invoke-WebRequest -Uri $chpSbUrl -OutFile $engine -UseBasicParsing
        if (Test-Path $engine) {
            Write-Host "Движок sing-box успешно скачан из релиза $Repo."
            $installedEngine = $true
        }
    } catch { }

    if (-not $installedEngine) {
        try {
            $headers = @{ "User-Agent" = "CehoProxy-Installer" }
            $sbRelease = Invoke-RestMethod -Uri "https://api.github.com/repos/SagerNet/sing-box/releases/latest" -Headers $headers -UseBasicParsing
            $sbAsset = $sbRelease.assets | Where-Object { $_.name -match 'sing-box-.*-windows-amd64\.zip$' } | Select-Object -First 1
            if ($sbAsset) {
                $zipTmp = Join-Path $env:TEMP 'sing-box-download.zip'
                $unzipTmp = Join-Path $env:TEMP 'sing-box-extract'
                Write-Host "Скачиваю $($sbAsset.name)..."
                Invoke-WebRequest -Uri $sbAsset.browser_download_url -OutFile $zipTmp -UseBasicParsing
                if (Test-Path $unzipTmp) { Remove-Item -Recurse -Force $unzipTmp }
                Expand-Archive -Path $zipTmp -DestinationPath $unzipTmp -Force
                $foundSb = Get-ChildItem -Path $unzipTmp -Filter 'sing-box.exe' -Recurse | Select-Object -First 1
                if ($foundSb) {
                    Copy-Item -Path $foundSb.FullName -Destination $engine -Force
                    Write-Host "Движок sing-box успешно установлен: $engine"
                }
                Remove-Item -Force $zipTmp -ErrorAction SilentlyContinue
                Remove-Item -Recurse -Force $unzipTmp -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Warning "Не удалось автоматически загрузить sing-box из сети: $_"
        }
    }
} elseif (Test-Path $engine) {
    Write-Host "Движок sing-box уже установлен."
}

Write-Host "Страница продукта: https://github.com/$Repo"

& $exe install --with-engine
