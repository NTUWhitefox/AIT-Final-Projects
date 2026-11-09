"""List working-tree files larger than 100 MB (100,000,000 bytes).

Usage (from any working directory):
    python Tools/git-large-file-checker.py
    python Tools/git-large-file-checker.py --all

By default, check tracked files and untracked files not ignored by Git.
--all also checks ignored files, but always skips .git directories.
This checks current files on disk, not staged blobs or commit history.
Exit codes: 0 = no large files, 1 = large files found, 2 = scan error.
"""

import argparse
import os
from pathlib import Path
import stat
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
LIMIT_BYTES = 100_000_000


def git_files():
    """Use NUL-delimited paths to preserve spaces, Unicode, and newlines."""
    result = subprocess.run(
        ["git", "-C", str(ROOT), "ls-files", "--cached", "--others",
         "--exclude-standard", "-z"],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    return {ROOT / os.fsdecode(name) for name in result.stdout.split(b"\0") if name}


def all_files():
    def raise_error(error):
        raise error

    for directory, dirs, files in os.walk(ROOT, onerror=raise_error):
        dirs[:] = [name for name in dirs if name.lower() != ".git"]
        for name in files:
            if name.lower() != ".git":
                yield Path(directory) / name


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--all", action="store_true", dest="include_ignored",
                        help="include ignored files (except .git metadata)")
    args = parser.parse_args(argv)
    large_files = []
    errors = []
    try:
        paths = all_files() if args.include_ignored else git_files()
        for path in paths:
            try:
                info = path.lstat()
            except FileNotFoundError:
                # Tracked files can be deleted in the working tree.
                continue
            except OSError as error:
                errors.append(str(error))
                continue
            # Skip symlinks and directories (including submodule entries).
            if stat.S_ISREG(info.st_mode) and info.st_size > LIMIT_BYTES:
                large_files.append((info.st_size, path.relative_to(ROOT).as_posix()))
    except subprocess.CalledProcessError as error:
        errors.append(os.fsdecode(error.stderr).strip())
    except OSError as error:
        errors.append(str(error))

    scope = "all files except .git" if args.include_ignored else "Git tracked + non-ignored untracked files"
    print(f"Root: {ROOT}")
    print(f"Scope: {scope}")
    print("Threshold: > 100 MB (100,000,000 bytes)")
    for size, name in sorted(large_files, key=lambda item: (-item[0], item[1])):
        print(f"{size / 1_000_000:10.2f} MB  {size:>15,} bytes  {name!r}")
    print(f"Found {len(large_files)} file(s) larger than 100 MB.")
    for error in errors:
        print(f"Scan error: {error}", file=sys.stderr)
    return 2 if errors else int(bool(large_files))


if __name__ == "__main__":
    sys.exit(main())
