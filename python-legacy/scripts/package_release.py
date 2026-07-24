"""
package_release.py

自动化打包脚本（用于 Windows，需在 python-legacy/ 目录下运行）：
1. 调用 `uv run pyinstaller --distpath Release/ -w -i logo.ico --clean .\\smart_onmyoji_start.py` 完成打包
2. 将仓库根目录下的 `img`(与 python-legacy/ 同级)和 python-legacy/ 下的 `modules` 复制到打包输出目录的 `_internal/` 下

支持 --dry-run 参数（只执行复制动作，不实际运行 pyinstaller）方便测试。
"""
import argparse
import os
import shutil
import subprocess
import sys


def run_pyinstaller(distpath: str, entry: str, icon: str, dry_run: bool) -> None:
    cmd = [
        "uv",
        "run",
        "pyinstaller",
        "--distpath",
        distpath,
        # Do not ask for confirmation when overwriting an existing build
        "--noconfirm",
        "-w",
        "-i",
        icon,
        "--clean",
        entry,
    ]

    # This script builds the default pyinstaller layout (one-folder).
    # Single-file (-F) support was removed to keep distribution stable when
    # copying auxiliary folders into the app's _internal directory.

    print("Packaging command:", " ".join(cmd))
    if dry_run:
        print("--dry-run: skipping pyinstaller execution")
        return

    try:
        subprocess.run(cmd, check=True)
    except subprocess.CalledProcessError as e:
        print("pyinstaller failed:", e)
        raise


def copy_into_internal(app_name: str, distpath: str, legacy_root: str, repo_root: str):
    """
    Copy `img` and `modules` into the release `_internal` folder.

    Behavior:
    - If `distpath/<app_name>` exists (one-dir build), copy into `distpath/<app_name>/_internal`.
    - Otherwise, create / use `distpath/_internal` for both onefile and other layouts.
    """

    # Always copy into Release/<app_name>/_internal. This keeps artifacts grouped
    # per-application even when pyinstaller created a single-file exe or when
    # the build folder doesn't exist yet (e.g. --dry-run).
    target_dir = os.path.join(distpath, app_name)
    internal = os.path.join(target_dir, "_internal")

    # Ensure the parent folder exists. We want Release/<app_name>/_internal always
    # created so the runtime can find _internal next to the app bundle/exe.
    os.makedirs(internal, exist_ok=True)

    # `img` lives at the true repo root (sibling of python-legacy/); `modules`
    # lives inside python-legacy/ itself.
    to_copy = [("img", repo_root), ("modules", legacy_root)]

    for name, root in to_copy:
        src = os.path.join(root, name)
        dst = os.path.join(internal, name)

        if not os.path.exists(src):
            print(f"Source folder not found, skipping: {src}")
            continue
        # If the destination already exists we skip copying to avoid repeated
        # overwrite prompts or churn. This prevents the "file exists" message
        # and keeps existing assets intact.
        if os.path.exists(dst):
            print(f"Destination already exists, skipping: {dst}")
            continue

        print(f"Copying {src} -> {dst}")
        # copytree with dirs_exist_ok True available in Python 3.8+
        try:
            shutil.copytree(src, dst, dirs_exist_ok=True)
        except TypeError:
            # Fallback for older Python: copy into a fresh folder
            shutil.copytree(src, dst)


def main():
    parser = argparse.ArgumentParser(description="Build release with pyinstaller and copy auxiliary folders into _internal")
    parser.add_argument("--distpath", default="Release/", help="PyInstaller dist path")
    parser.add_argument("--entry", default="smart_onmyoji_start.py", help="Entry script to build")
    parser.add_argument("--icon", default="logo.ico", help="Icon path for exe")
    parser.add_argument("--name", default="smart_onmyoji_start", help="Built app folder name under distpath")
    # --onefile support removed: the release layout produced by default is
    # one-directory which keeps the _internal/ sibling folder accessible.
    parser.add_argument("--dry-run", action="store_true", help="Don't run pyinstaller, only do the copy step (useful for testing)")

    args = parser.parse_args()

    legacy_root = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
    repo_root = os.path.abspath(os.path.join(legacy_root, os.pardir))
    distpath = os.path.abspath(args.distpath)

    try:
        run_pyinstaller(distpath, args.entry, args.icon, args.dry_run)

        # After pyinstaller, ensure we copy img and modules into Release/<name>/_internal
        copy_into_internal(args.name, distpath, legacy_root, repo_root)

        print("Packaging finished — img and modules copied into _internal.")
    except Exception as e:
        print("ERROR:", e)
        sys.exit(1)


if __name__ == "__main__":
    main()
