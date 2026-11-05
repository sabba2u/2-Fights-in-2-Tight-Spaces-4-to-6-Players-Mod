"""Build the drop-in release zip: BepInEx 6 be.791 (IL2CPP, x64) + the mod, ready to extract into the game folder.

    python release/make_release.py            # -> dist/FITS2-4to6Players-v<version>.zip

Takes BepInEx from tools/bepinex/be791 (the unmodified build download) -- never from the game folder, which also
holds files BepInEx generated from the game (interop, unity-libs, cache) and personal config/logs.
"""
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BEPINEX = ROOT / "tools" / "bepinex" / "be791"
PLUGIN_DIR = ROOT / "plugin" / "FourPlayers"
PLUGIN_DLL = PLUGIN_DIR / "bin" / "Release" / "net6.0" / "FourPlayers.dll"
HERE = Path(__file__).resolve().parent
DIST = ROOT / "dist"

# Keep BepInEx's console window on: it opens next to the game, so players can see at a glance that the mod loaded
# (and the "CapPatch: ... player cap 6" line). The log also goes to BepInEx\LogOutput.log.
BEPINEX_CFG = """[Logging.Console]

## Enables showing a console for log output.
# Setting type: Boolean
# Default value: true
Enabled = true
"""


def version():
    m = re.search(r'const string Version = "([^"]+)"', (PLUGIN_DIR / "Plugin.cs").read_text("utf-8"))
    if not m:
        raise SystemExit("version not found in Plugin.cs")
    return m.group(1)


def main():
    if not (BEPINEX / "winhttp.dll").exists():
        raise SystemExit(f"BepInEx build not found at {BEPINEX}")
    if not PLUGIN_DLL.exists():
        raise SystemExit("Build the plugin first: dotnet build -c Release (in plugin/FourPlayers)")
    v = version()
    DIST.mkdir(exist_ok=True)
    out = DIST / f"FITS2-4to6Players-v{v}.zip"
    n = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f in sorted(BEPINEX.rglob("*")):
            if f.is_file() and f.name != "changelog.txt":
                z.write(f, f.relative_to(BEPINEX).as_posix()); n += 1
        z.write(PLUGIN_DLL, "BepInEx/plugins/FourPlayers.dll"); n += 1
        z.writestr("BepInEx/config/BepInEx.cfg", BEPINEX_CFG); n += 1
        z.write(HERE / "LICENSE-BepInEx.txt", "BepInEx/LICENSE-BepInEx.txt"); n += 1
        z.write(HERE / "INSTALL.txt", "FITS2-4to6Players-INSTALL.txt"); n += 1
    print(f"{out}  ({n} files, {out.stat().st_size / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
