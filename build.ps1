# Gera o Lume.exe e o Instalar-Lume.exe a partir de Lume.cs + lume.html + ícone (tudo embutido no programa).
# Uso: clique com o botão direito > "Executar com o PowerShell", ou:
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-OutDir pasta]
# Sem -OutDir, o resultado fica na pasta dist\ ao lado deste script.
param([string]$OutDir = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
New-Item -ItemType Directory -Force $OutDir | Out-Null
$exe = Join-Path $OutDir 'Lume.exe'
$log = Join-Path $here 'build.log'
"build $(Get-Date)" | Out-File $log -Encoding utf8

try {
  # Ícone (lume.ico para o programa, lume.png para a janela/barra de tarefas)
  & (Join-Path $here 'icon.ps1') $here
  "icone ok" | Out-File $log -Append -Encoding utf8

  # O Windows não deixa substituir um programa em uso: encerra o Lume se ainda estiver rodando.
  # (Livros, progresso e anotações já ficam gravados no disco, nada se perde.)
  $running = Get-Process -Name 'Lume' -ErrorAction SilentlyContinue
  if ($running) {
    "encerrando Lume em execucao (pid $($running.Id -join ', '))" | Out-File $log -Append -Encoding utf8
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
  }
  # Fecha também as janelas do Lume (Edge aberto com o perfil do Lume), que ficariam sem servidor.
  Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*\Lume\dados\janela*' } |
    ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }

  # Se ainda assim estiver preso, tira o arquivo antigo do caminho (renomear é permitido mesmo em uso).
  if (Test-Path $exe) {
    try { Remove-Item $exe -Force -ErrorAction Stop }
    catch {
      $old = Join-Path $here ('Lume-antigo-' + (Get-Date -Format 'yyyyMMddHHmmss') + '.exe')
      Move-Item $exe $old -Force
      "Lume.exe antigo movido para $old" | Out-File $log -Append -Encoding utf8
    }
  }
  Get-ChildItem $here -Filter 'Lume-antigo-*.exe' -ErrorAction SilentlyContinue | ForEach-Object { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch {} }

  # Bibliotecas de leitura embutidas no programa (o Lume.exe nunca baixa nada).
  # Ficam em codigo\bibliotecas; se faltarem, são copiadas da instalação anterior ou baixadas uma vez aqui.
  $libs = @('cdnjs/pdf.js/3.11.174/pdf.min.js', 'cdnjs/pdf.js/3.11.174/pdf.worker.min.js', 'cdnjs/jszip/3.10.1/jszip.min.js', 'jsd/epubjs@0.3.93/dist/epub.min.js')
  $libDir = Join-Path $here 'bibliotecas'
  $oldCache = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Lume\dados\bibliotecas'
  $resArgs = @()
  foreach ($l in $libs) {
    $rel = $l -replace '/', '\'
    $local = Join-Path $libDir $rel
    if (-not (Test-Path $local)) {
      New-Item -ItemType Directory -Force (Split-Path $local) | Out-Null
      if (Test-Path (Join-Path $oldCache $rel)) { Copy-Item (Join-Path $oldCache $rel) $local }
      else {
        $url = if ($l.StartsWith('cdnjs/')) { 'https://cdnjs.cloudflare.com/ajax/libs/' + $l.Substring(6) } else { 'https://cdn.jsdelivr.net/npm/' + $l.Substring(4) }
        [Net.ServicePointManager]::SecurityProtocol = 3072
        Invoke-WebRequest $url -OutFile $local -UseBasicParsing
      }
    }
    $resArgs += "/resource:$local,cdn/$l"
  }

  # Kit do WebView2 (pacote oficial da Microsoft no NuGet): a janela do Lume usa o componente
  # WebView2 que já vem no Windows. Baixado uma vez para codigo\webview2.
  $wvVer = '1.0.2651.64'
  $wvDir = Join-Path $here 'webview2'
  $wvFiles = @{ core = (Join-Path $wvDir 'Microsoft.Web.WebView2.Core.dll'); forms = (Join-Path $wvDir 'Microsoft.Web.WebView2.WinForms.dll'); loader = (Join-Path $wvDir 'WebView2Loader.dll') }
  if (-not ((Test-Path $wvFiles.core) -and (Test-Path $wvFiles.forms) -and (Test-Path $wvFiles.loader))) {
    New-Item -ItemType Directory -Force $wvDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = 3072
    $pkg = Join-Path $wvDir "webview2-$wvVer.zip"
    Invoke-WebRequest "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$wvVer" -OutFile $pkg -UseBasicParsing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($pkg)
    $libs45 = $zip.Entries | Where-Object { $_.FullName -match '^lib/net4[0-9]*/Microsoft\.Web\.WebView2\.(Core|WinForms)\.dll$' } | Sort-Object FullName -Descending
    foreach ($name in 'Core', 'WinForms') {
      $e = $libs45 | Where-Object { $_.FullName -like "*Microsoft.Web.WebView2.$name.dll" } | Select-Object -First 1
      "webview2: $($e.FullName)" | Out-File $log -Append -Encoding utf8
      [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $wvDir "Microsoft.Web.WebView2.$name.dll"), $true)
    }
    $ld = $zip.Entries | Where-Object { $_.FullName -eq 'runtimes/win-x64/native/WebView2Loader.dll' } | Select-Object -First 1
    [IO.Compression.ZipFileExtensions]::ExtractToFile($ld, $wvFiles.loader, $true)
    $zip.Dispose()
  }
  # Os três arquivos ficam ao lado do Lume.exe
  $outDir = Split-Path -Parent $exe
  foreach ($f in $wvFiles.Values) { Copy-Item $f $outDir -Force }

  $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  # /codepage:65001 = lê os .cs como UTF-8 (sem isso os acentos dos textos podem sair estragados)
  $out = & $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32icon:$here\lume.ico" "/resource:$here\lume.html,index.html" "/resource:$here\lume.png,icon.png" @resArgs /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$($wvFiles.core)" "/r:$($wvFiles.forms)" "$here\Lume.cs" 2>&1
  $out | Out-File $log -Append -Encoding utf8
  "csc exit $LASTEXITCODE -> $exe" | Out-File $log -Append -Encoding utf8

  # Instalador para enviar a outras pessoas: um único arquivo com o Lume.exe dentro.
  if ($LASTEXITCODE -eq 0 -and (Test-Path (Join-Path $here 'Instalador.cs'))) {
    $setup = Join-Path $OutDir 'Instalar-Lume.exe'
    if (Test-Path $setup) { Remove-Item $setup -Force }
    $out = & $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$setup" "/win32icon:$here\lume.ico" "/resource:$exe,Lume.exe" "/resource:$here\lume.png,logo.png" "/resource:$($wvFiles.core),Microsoft.Web.WebView2.Core.dll" "/resource:$($wvFiles.forms),Microsoft.Web.WebView2.WinForms.dll" "/resource:$($wvFiles.loader),WebView2Loader.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$here\Instalador.cs" 2>&1
    $out | Out-File $log -Append -Encoding utf8
    "instalador exit $LASTEXITCODE -> $setup" | Out-File $log -Append -Encoding utf8
  }

  # Pede ao Windows para recarregar o cache de ícones (Explorer e barra de tarefas).
  try { Start-Process "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList '-show' -WindowStyle Hidden } catch {}
} catch {
  "ERRO: $_" | Out-File $log -Append -Encoding utf8
}
