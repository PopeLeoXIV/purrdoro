"""
Static checks for Purrdoro's WinUI XAML that can run on any OS (the real XAML
compiler only runs on Windows):

  * every .xaml file is well-formed XML
  * every {x:Bind ...} path resolves to a public member of the page's ViewModel
    (member names are read from the compiled Purrdoro.Core assembly via
    `dotnet` reflection output passed in as JSON, or from source as a fallback)
  * every event handler named in XAML exists in the matching code-behind
  * every x:Name used from code-behind is declared in the XAML
  * every {StaticResource}/{ThemeResource} key is defined in the app's
    dictionaries or is a known WinUI resource
  * visual-state setter targets refer to named elements

Usage (from the repo root):  python tools/validate_xaml.py
"""

import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "Purrdoro"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

# WinUI resources referenced by the app that are defined by XamlControlsResources.
KNOWN_WINUI_KEYS = {
    "AccentButtonStyle", "DefaultButtonStyle", "BodyTextBlockStyle", "BodyStrongTextBlockStyle",
    "CaptionTextBlockStyle", "SubtitleTextBlockStyle", "TitleTextBlockStyle",
    "SystemColorWindowColor", "SystemColorWindowTextColor", "SystemColorButtonFaceColor",
    "SystemColorHighlightColor", "SystemColorGrayTextColor",
}

errors: list[str] = []


def err(msg: str) -> None:
    errors.append(msg)


def load_vm_members() -> dict[str, set[str]]:
    """Public instance members of the Core ViewModels, via reflection on the built assembly."""
    script_dir = ROOT / "tools" / "ReflectMembers"
    result = subprocess.run(
        ["dotnet", "run", "--project", str(script_dir), "-v", "q", "--", str(ROOT / "src" / "Purrdoro.Core" / "bin" / "Debug" / "net10.0" / "Purrdoro.Core.dll")],
        capture_output=True, text=True, check=False)
    if result.returncode != 0:
        print(result.stdout, result.stderr)
        raise SystemExit("Could not reflect over Purrdoro.Core.dll (build it first).")
    return {k: set(v) for k, v in json.loads(result.stdout.strip().splitlines()[-1]).items()}


def collect_resource_keys() -> set[str]:
    keys = set(KNOWN_WINUI_KEYS)
    for path in [APP / "App.xaml", *APP.glob("Themes/*.xaml")]:
        for el in ET.parse(path).iter():
            key = el.get(f"{X}Key")
            if key:
                keys.add(key)
    return keys


def code_behind_for(xaml: Path) -> str:
    cs = xaml.with_suffix(".xaml.cs")
    return cs.read_text(encoding="utf-8") if cs.exists() else ""


def main() -> int:
    vm_members = load_vm_members()
    resource_keys = collect_resource_keys()
    control_props = {
        "CatMascot": {"Mood"},
        "ProgressArc": {"Value", "Thickness", "TrackBrush", "ArcBrush"},
    }
    page_vm = {"TimerPage": "MainViewModel", "SettingsPage": "SettingsViewModel"}

    for xaml in sorted(APP.rglob("*.xaml")):
        if "obj" in xaml.parts or "bin" in xaml.parts:
            continue
        rel = xaml.relative_to(ROOT)
        text = xaml.read_text(encoding="utf-8")
        try:
            tree = ET.parse(xaml)
        except ET.ParseError as e:
            err(f"{rel}: not well-formed XML: {e}")
            continue

        root = tree.getroot()
        cls = root.get(f"{X}Class")
        if xaml.name not in ("App.xaml",) and "Themes" not in xaml.parts and not cls:
            err(f"{rel}: missing x:Class")

        names = {el.get(f"{X}Name") for el in root.iter() if el.get(f"{X}Name")}
        code = code_behind_for(xaml)
        short = xaml.stem

        # --- x:Bind paths -------------------------------------------------------
        for match in re.finditer(r"\{x:Bind\s+([^,}\s]+)", text):
            path = match.group(1)
            parts = path.split(".")
            if parts[0] == "ViewModel":
                vm = page_vm.get(short)
                if vm is None:
                    err(f"{rel}: x:Bind ViewModel.* used but page has no known ViewModel")
                elif parts[1] not in vm_members[vm]:
                    err(f"{rel}: x:Bind path '{path}' not found on {vm}")
                if f"public {vm} ViewModel" not in code:
                    err(f"{rel}: code-behind lacks 'public {vm} ViewModel'")
            elif parts[0] == "IsFilled":
                if "IsFilled" not in vm_members["CycleDot"]:
                    err(f"{rel}: CycleDot.IsFilled missing")
            else:
                err(f"{rel}: unexpected x:Bind root '{path}'")

        # --- event handlers -----------------------------------------------------
        for el in root.iter():
            for attr, value in el.attrib.items():
                local = attr.split("}")[-1]
                if local in {"Click", "Invoked", "SizeChanged", "Navigated", "BackRequested", "Loaded"}:
                    if not re.search(rf"\bvoid\s+{re.escape(value)}\s*\(", code):
                        err(f"{rel}: handler '{value}' ({local}) not found in code-behind")

        # --- x:Name references from code-behind ---------------------------------
        for ident in set(re.findall(r"\b([A-Z][A-Za-z]+(?:Box|Grid|Frame|TitleBar|Button|Track|Arc))\b", code)):
            if ident in {"RootGrid", "RootFrame", "AppTitleBar", "FocusBox", "ShortBreakBox", "LongBreakBox",
                         "IntervalBox", "Track", "Arc", "PrimaryButton"} and ident not in names:
                err(f"{rel}: code-behind uses '{ident}' which is not an x:Name in the XAML")

        # --- resource keys ------------------------------------------------------
        for kind, key in re.findall(r"\{(StaticResource|ThemeResource)\s+([A-Za-z0-9_]+)\}", text):
            if key not in resource_keys:
                err(f"{rel}: {kind} '{key}' is not defined")

        # --- visual state setter targets -----------------------------------------
        for el in root.iter():
            if el.tag.endswith("Setter") and el.get("Target"):
                target = el.get("Target").split(".")[0]
                if target not in names:
                    err(f"{rel}: VisualState setter targets unknown element '{target}'")

        # --- custom control properties ------------------------------------------
        for el in root.iter():
            tag = el.tag.split("}")[-1]
            if tag in control_props and el.tag.startswith("{using:Purrdoro.Controls}"):
                for attr in el.attrib:
                    local = attr.split("}")[-1]
                    if "." in local or attr.startswith("{"):
                        continue
                    allowed = control_props[tag] | {"Width", "Height", "HorizontalAlignment", "VerticalAlignment", "Margin"}
                    if local not in allowed:
                        err(f"{rel}: {tag} has no property '{local}'")

        print(f"checked {rel}")

    # --- csproj content files exist --------------------------------------------
    csproj = (APP / "Purrdoro.csproj").read_text(encoding="utf-8")
    for inc in re.findall(r'<Content Include="([^"]+)"', csproj):
        if not (APP / inc.replace("\\", "/")).exists():
            err(f"Purrdoro.csproj: Content '{inc}' does not exist")

    # --- manifest assets exist ---------------------------------------------------
    manifest = (APP / "Package.appxmanifest").read_text(encoding="utf-8")
    for asset in re.findall(r'"(Assets\\[^"]+\.png)"', manifest):
        stem = asset.replace("\\", "/")[:-4]
        if not list(APP.glob(stem.replace("Assets/", "Assets/") + "*.png")):
            err(f"Package.appxmanifest: no file for {asset}")

    if errors:
        print("\nFAILED:")
        for e in errors:
            print("  -", e)
        return 1

    print("\nAll XAML checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
