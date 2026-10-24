$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$webview = Join-Path $env:APPDATA 'Python\Python313\site-packages\webview\lib'
$required = @(
    (Join-Path $webview 'Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $webview 'Microsoft.Web.WebView2.WinForms.dll'),
    (Join-Path $webview 'runtimes\win-x64\native\WebView2Loader.dll')
)
foreach ($file in @($compiler) + $required) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing dependency: $file" }
}
$out = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = Join-Path $out '学习强国.exe'
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    "/out:$exe", '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.Security.dll',
    "/win32icon:$(Join-Path $PSScriptRoot 'assets\app.ico')",
    "/resource:$(Join-Path $PSScriptRoot 'assets\app.ico'),app.ico",
    "/reference:$($required[0])", "/reference:$($required[1])",
    "/resource:$($required[0]),Microsoft.Web.WebView2.Core.dll",
    "/resource:$($required[1]),Microsoft.Web.WebView2.WinForms.dll",
    "/resource:$($required[2]),WebView2Loader.dll",
    "/resource:$(Join-Path $PSScriptRoot 'browser.js'),browser.js",
    "/resource:$(Join-Path $PSScriptRoot 'login-preview.js'),login-preview.js",
    (Join-Path $PSScriptRoot 'Core.cs'), (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'Tests.cs'), (Join-Path $PSScriptRoot 'BrowserVerification.cs'),
    (Join-Path $PSScriptRoot 'Dashboard.cs'), (Join-Path $PSScriptRoot 'LoginPreview.cs'),
    (Join-Path $PSScriptRoot 'VideoVerification.cs'),
    (Join-Path $PSScriptRoot 'Tray.cs'), (Join-Path $PSScriptRoot 'LoginSession.cs'),
    (Join-Path $PSScriptRoot 'SessionVerification.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
$process = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Native tests failed: $($process.ExitCode)" }
Push-Location $PSScriptRoot
try {
    node test_browser.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Browser script tests failed' }
    node test_login_preview.cjs
    if ($LASTEXITCODE -ne 0) { throw 'QR preview script tests failed' }
} finally { Pop-Location }
Get-Item -LiteralPath $exe | Select-Object FullName,Length
Get-FileHash -LiteralPath $exe -Algorithm SHA256
