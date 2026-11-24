[CmdletBinding()]
param([string]$Executable = (Join-Path $PSScriptRoot '..\out\Windows Voice Access Full Numbers.exe'),
    [switch]$Browser, [string]$Chrome = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe")
$ErrorActionPreference = 'Stop'
$trialName = 'full-numbers-input-' + [guid]::NewGuid()
$parent = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName.TrimEnd('\')
$trial = [IO.Directory]::CreateDirectory((Join-Path $parent $trialName)).FullName
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$library = Join-Path $trial 'Windows Voice Access Full Numbers.exe'
Copy-Item -LiteralPath $Executable -Destination $library
$references = @($library, 'System.Windows.Forms.dll', 'System.Drawing.dll', "$framework\WPF\WindowsBase.dll",
    "$framework\WPF\UIAutomationClient.dll", "$framework\WPF\UIAutomationTypes.dll") | ForEach-Object { "/r:$_" }
$test = Join-Path $trial 'InputDesktopTest.exe'
& "$framework\csc.exe" /nologo /target:exe "/out:$test" @references (Join-Path $PSScriptRoot 'InputDesktopTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the scratch window test.' }
if ($Browser) { & $test $trial $Chrome (Join-Path $PSScriptRoot 'popup-test.html') }
else { & $test $trial }
if ($LASTEXITCODE -ne 0) { throw "Scratch test failed. Test events are in $trial" }
$completed = Get-Item -LiteralPath $trial
if ($completed.Parent.FullName -eq $parent -and $completed.Name -eq $trialName) {
    Remove-Item -LiteralPath $completed.FullName -Recurse -Force
}
