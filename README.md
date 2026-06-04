# Clicky

![Clicky logo](ClickyPlugin/src/package/metadata/Icon256x256.png)

Clicky is a free and open-source Logitech Options+ plugin that adds haptic feedback to supported mouse clicks.

Homepage: https://clicky.dzintarsit.lv/

## Requirements

- Windows
- .NET 8 SDK
- Logitech Options+
- `PluginApi.dll` available through a local Logi Plugin Service install

## Build

From the solution:

```powershell
dotnet build .\ClickyPlugin\ClickyPlugin.sln
```

Or from the plugin project directly:

```powershell
dotnet build .\ClickyPlugin\src\ClickyPlugin.csproj
```

The plugin build publishes `ClickyInputHelper` and copies it into the packaged plugin output automatically.

## Repository Layout

```text
ClickyPlugin/
  ClickyPlugin.sln
  manifest.json
  src/
ClickyInputHelper/
LICENSE
NOTICE
README.md
```

## Troubleshooting

### The settings page does not connect

- Open Clicky from Logitech Options+ first.
- Check `http://127.0.0.1:65439/health` on the same machine.
- Reload `https://clicky.dzintarsit.lv/settings/` after the plugin is open.

### The input helper is not running

- Reopen Logitech Options+ and then reopen the settings page.
- Check the helper heartbeat file:
  `C:\Users\<you>\AppData\Local\Logi\LogiPluginService\Temp\ClickyInputHelper.heartbeat`

### Haptics do not fire

- Make sure haptics are enabled.
- Test a waveform with the Preview button first.
- Confirm the MX Master 4 is the active supported device.
- If the device filter is active, verify the correct mouse is selected.

### The wrong mouse was selected

- Open the settings page again.
- Choose the correct device.
- Clear saved Clicky data and bind again if needed.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

Apache License 2.0. See `LICENSE` and `NOTICE`.
