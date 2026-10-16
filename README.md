# Ubisoft Auto-Login

A small Windows tray utility that fills in Ubisoft Connect's **"Re-enter your password"** prompt for you, so Ubisoft games launched from Steam (Big Picture or the Xbox full-screen experience) start without needing a keyboard.

It automates the same thing you would do by hand, typing your own password into the genuine Ubisoft Connect prompt. It does **not** bypass authentication, patch Ubisoft software, inject code, read network traffic, or touch two-factor/CAPTCHA screens.

> Not affiliated with or endorsed by Ubisoft or Valve. "Ubisoft" and "Ubisoft Connect" are trademarks of Ubisoft Entertainment.

## How it works

| Step | What happens |
|---|---|
| Detect | An out-of-context WinEvent hook (no injection, no polling) reports windows as they appear. Everything except `upc.exe` is ignored immediately. |
| Verify | The window must be the prompt window class (`uplay_start`), owned by `upc.exe` in the Ubisoft Connect install folder from the registry, with a valid Authenticode signature from Ubisoft Entertainment, running in your Windows session. The window title is never used. |
| Read | The prompt is custom-drawn and exposes **no** UI Automation controls, so the built-in Windows OCR engine reads it in memory. The image and text are never saved or logged. Auto-login continues only when it sees the known password screen with the e-mail pre-filled and the password field empty. |
| Type | The password is read from Windows Credential Manager and typed with `SendInput`. Before **every** keystroke it re-checks that the verified prompt is still the foreground window, and stops instantly if not. No clipboard is used. Then it presses Enter. |
| Confirm | Success = the prompt closes within 20 s. If it stays open, auto-login **pauses** until you re-save credentials or toggle it, so a wrong password is never submitted twice. A new prompt after a successful login (e.g. relaunching a game) is handled normally; only 3 prompts within 2 minutes counts as a loop and pauses it. |

Two-factor, CAPTCHA, account recovery, network errors and any unrecognised screen are always left to you, with one tray notification.

### What was verified (Ubisoft Connect 174.2, Windows 11, English UI)

- The prompt belongs to `upc.exe` (window class `uplay_start`), not the game. The rest of Ubisoft Connect is Chromium (CEF), but this prompt is not.
- `upc.exe` runs non-elevated, so the tool needs no admin rights.
- UI Automation exposes only the bare window: no password field and no button.
- Keystrokes posted to the window in the background are ignored. When the prompt is the foreground window, the password field already has focus and accepts typed input.
- Windows OCR reliably reads the heading, labels and placeholder.

Not yet verified: that Enter submits the prompt (it is the standard default; the first real run confirms it), other UI languages (the text checks are English), and other Ubisoft Connect versions. If Ubisoft changes the prompt, the tool stops acting and tells you instead of guessing.

## Install

1. Download `UbisoftAutoLogin.exe` (self-contained, no .NET install needed) and put it in a folder such as `%LOCALAPPDATA%\Programs\UbisoftAutoLogin`.
2. Run it. The settings window opens the first time: enter your Ubisoft e-mail and password and press **Save credentials**.
3. Optional: tick **Start with Windows** (off by default).
4. Launch a Ubisoft game from Steam. The prompt is filled in automatically.

The tray icon is the Ubisoft Connect icon from your own installation (read at runtime, not shipped with this project) with a status dot: green = ready, blue = logging in, amber = needs you, greyed = off.

Tray menu: Auto-login enabled · Configure Ubisoft credentials · Test login detection · View status · View logs · Start with Windows (also Xbox mode) · Exit.

### Controller / Xbox full-screen / Big Picture notes

- Nothing needs a keyboard during normal use. The utility has no visible windows unless you open settings.
- When a game launches, Ubisoft Connect brings the prompt to the front itself. If it doesn't, the utility brings that one verified window to the front before typing (you can turn this off with `BringPromptToFront` in `settings.json`).
- Notifications appear only when you must act, once per prompt. Windows may hold them back while a full-screen game runs.

## Security notes and limits

- The password lives in **Windows Credential Manager** as a Generic credential (`UbisoftAutoLogin:UbisoftConnect`), encrypted with your Windows account's DPAPI key and not roaming. Other Windows users can't read it.
- **Any program running under your Windows account can read it**, just like your browser's saved passwords. That is the limit of local credential storage: if malware runs as you, it can take it. Use a unique Ubisoft password and enable Ubisoft 2FA. The tool never handles 2FA codes.
- The password is held in memory only for the time needed to type it, then cleared. It is never written to files, logs, command lines or the clipboard.
- Logs (`%LOCALAPPDATA%\UbisoftAutoLogin\autologin.log`) contain events only. E-mail addresses and token-like strings are redacted automatically.
- Keystrokes go only to a window that passes every verification check, and only while it is the foreground window.

## Diagnostics

`UbisoftAutoLoginDiagnostics.exe` helps when Ubisoft changes something:

- **Inspect now / Export report**: sanitized report with process identity, window classes, and the UI Automation tree. Field values are never read.
- **Watch & auto-capture**: saves a report whenever a Ubisoft-signed window appears.
- Command line: `--report out.md [--all]`, `--watch <dir> [minutes]` (no window), `--classify out.txt [--verbose]`.

Reports are written to `Documents\UbisoftAutoLogin`. `--classify --verbose` prints the OCR text with e-mails redacted. Check a report before sharing it.

## Troubleshooting

| Status / symptom | What to do |
|---|---|
| *Manual action required* | Read the notification (2FA, CAPTCHA, unknown screen, prompt lost focus). Finish that login by hand. |
| *Paused (check password)* | Ubisoft rejected the login or asked again right away. Re-save the correct password in settings. |
| Nothing happens | Tray → **Test login detection** while the prompt is open. Check the log. A non-English Ubisoft UI is not supported yet. |
| "running elevated" | If Ubisoft Connect runs as administrator, Windows blocks input from normal apps. Run Ubisoft Connect normally. |
| Fallback | If auto-login can't work on your setup, bind Steam Input's on-screen keyboard (Steam + X) or the Windows touch keyboard to a controller button. |

## Build from source

Requirements: Windows 10 2004+ / 11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
./build.ps1            # runs tests, publishes self-contained exes to ./publish
./build.ps1 -SkipTests
```

Projects:

- `src/UbisoftAutoLogin.Core`: verification, detection, OCR classification, Credential Manager, typing, engine
- `src/UbisoftAutoLogin.Tray`: tray app and settings window (`UbisoftAutoLogin.exe`)
- `src/UbisoftAutoLogin.Diagnostics`: diagnostic tool (UI Automation inspector)
- `tests/UbisoftAutoLogin.Tests`: unit tests (classifier, redaction, credential round-trip, verification)

The exe is ~55 MB because it bundles the .NET runtime, WinForms and the Windows OCR API projection.

## License

MIT. See [LICENSE](LICENSE).
