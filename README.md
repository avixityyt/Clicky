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

## License

Apache License 2.0. See `LICENSE` and `NOTICE`.
