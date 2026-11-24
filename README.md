# Windows Voice Access Full Numbers

Windows Voice Access Full Numbers lets you select an available Windows control by voice when a notification or modeless dialog is open. It draws blue number labels on controls that apps expose through Windows UI Automation. It runs when you open it and exits after selection, cancellation, or 60 seconds of inactivity.

## Install from source

On 64-bit Windows 11, run these commands in PowerShell. The installer uses the .NET Framework compiler included with Windows and installs the app for the current user.

```powershell
git clone --branch v0.1.0 https://github.com/brycelelbach/windows-voice-access-full-numbers.git
cd windows-voice-access-full-numbers
.\install.ps1
```

If your PowerShell execution policy blocks local scripts, you can run the installer in a separate process with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`.

The installer adds **Full Numbers** to your Start menu and places the verified executable and its license in `%LOCALAPPDATA%\Programs\Windows Voice Access Full Numbers`. Run the installer again to reinstall. To remove the app, delete the **Full Numbers** shortcut from your Start menu's Programs folder and delete the executable and license from the installation folder.

## Use with Voice Access

1. With the app or pop-up visible, say **“Open Full Numbers.”**
2. Wait for the black bar at the bottom center to say **“Type a label number.”** Read the blue label on the control you want. For **108**, say **“Type one hundred and eight.”** The bar selects that label automatically.
3. To filter the labels one digit at a time, say **“Press one,” “Press zero,” “Press eight.”** A complete label with no longer matches selects automatically while the bar has focus. Say **“Press Enter”** to confirm an ambiguous prefix immediately. Use **“Press Backspace”** to correct a digit or **“Press Escape”** to close.

If Windows keeps focus in the previous app, the bar offers **“Press F8”** or a click on the bar to enable dictation. Wait for **“Type a label number”** before speaking the number. When a native Windows menu needs to keep focus to stay open, the overlay shows the digit-by-digit instructions.

Full Numbers moves the pointer to an exposed part of the selected control and sends one left click. A text field receives the caret where the pointer lands. If the control moves, Full Numbers looks up its current bounds and checks what is under the pointer again. If it disappears or is fully covered, a brief message closes automatically. A true modal dialog disables its parent controls. Apps can also leave controls out of the accessibility tree.

## Reproduce the release

```powershell
$app = .\build.ps1
Get-FileHash -Algorithm SHA256 -LiteralPath $app.FullName
```

Each revision records its executable's SHA-256 in [release.json](release.json). To reproduce a particular build, check out its commit or release tag before running `build.ps1`. The .NET Framework C# compiler writes a new PE timestamp and module ID on every build. The build restores the two recorded values and checks the SHA-256 before copying the executable to `out`. The compiler used is Framework64 `csc.exe` version `4.8.9221.0`.

Use `.\build.ps1 -Development` to compile edited sources without the release comparison. The installer always verifies the release hash.

## Test

The Python 3 tests launch the supplied executable on a disposable Windows desktop. They check number entry with more than 108 labels, the blue badges and black bar, cancellation with Escape, and exclusion of a disabled parent behind a modal dialog. They also check that selection on an inactive desktop sends no physical input.

```powershell
$installed = Join-Path $env:LOCALAPPDATA 'Programs\Windows Voice Access Full Numbers\Windows Voice Access Full Numbers.exe'
python tests/isolated-test.py native --executable $installed
python tests/isolated-test.py modal --executable $installed
python tests/isolated-test.py browser --executable $installed
python tests/isolated-test.py numbers --executable $installed
```

The browser test uses a fresh Chrome profile. Pass `--chrome` with the path to Chrome if it is installed outside its standard location.

To test the actual click and keyboard focus, run these checks on an unlocked desktop. Each opens a temporary scratch window and moves the pointer. The tests load the supplied executable's assembly, select scratch controls through the number bar, and verify events in the receiving app. Cases include a slowly entered 108, a checkbox, text fields inside and outside a modal dialog, a button that moves, a button that is replaced, and a partly covered button in Chrome.

```powershell
.\tests\input-desktop-test.ps1 -Executable $installed
.\tests\input-desktop-test.ps1 -Executable $installed -Browser
```

To check actual Voice Access routing, open [the local test page](tests/popup-test.html) in a browser and focus its scratch field. Say **“Open Full Numbers”**, then dictate the blue label on **Browser background action** when the bar is ready. The button should change to **Activated background** and the scratch field should stay empty. Tests on a private desktop exercise directed text entry there. Live speech exercises system foreground routing.

## License

Windows Voice Access Full Numbers uses the [Apache License 2.0 with LLVM Exceptions](LICENSE). The SPDX identifier is `Apache-2.0 WITH LLVM-exception`.
