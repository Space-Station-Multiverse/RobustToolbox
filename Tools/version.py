#!/usr/bin/env python3

# Tools/version.py April2024 1.2.3

# This version of the script adds support for engine branches
# As of current writing, this is only for backport branches and otherwise main branch doesn't use a branch identifier
# ex:
# mv-1.0.0 is the mainline branch
# mv-April2024-1.0.0 is April2024 backports branch

import subprocess
import sys
import os
import argparse
import time
from typing import List

def main():
    parser = argparse.ArgumentParser(description = "Tool for versioning RobustToolbox: commits the version config update and sets your local tag.")
    parser.add_argument("branch", help = "Branch that will be written to tag. Ex: April2024")
    parser.add_argument("version", help = "Version that will be written to tag. Format: x.x.x")
    parser.add_argument("--file-only", action = "store_true", help = "Does not perform the Git part of the update (for writes only, not undos!)")
    parser.add_argument("--undo", action = "store_true", help = "Macro to rebase over last commit and remove version tag. Version still required.")

    result = parser.parse_args()

    version:   str  = result.version
    branch:    str  = result.branch
    undo:      bool = result.undo
    file_only: bool = result.file_only

    tagName = "mv-" + branch + "-" + version

    if branch == None:
        print("Branch required in this version of script (try --help)")
        sys.exit(1)

    if undo:
        undo_version(tagName)
    else:
        write_version(version, file_only, tagName)


def verify_version(version: str):
    parts = version.split(".")
    if len(parts) != 3:
        print("Version must be split into three parts with '.'")
        sys.exit(1)
    for v in parts:
        # this verifies parsability, exceptions here are expected for bad input
        int(v)

def write_version(version: str, file_only: bool, tagName:bool):
    # Writing operation
    if version == None:
        print("Version required for a writing operation (try --help)")
        sys.exit(1)

    # Verify
    verify_version(version)

    update_release_notes(tagName)

    # Update
    with open("MSBuild/Robust.Engine.Version.props", "w") as file:
        file.write("<Project>" + os.linesep)
        file.write("    <!-- This file automatically reset by Tools/version.py -->"  + os.linesep)
        file.write("    <PropertyGroup><Version>" + version + "</Version></PropertyGroup>" + os.linesep)
        file.write("</Project>" + os.linesep)

    if not file_only:
        # Commit
        subprocess.run(["git", "commit", "--allow-empty", "-m", "Version: " + version, "MSBuild/Robust.Engine.Version.props", "RELEASE-NOTES-SSMV.md"], check=True)

        # Tag
        subprocess.run(["git", "tag", tagName], check=True)
        print("Tagged as " + tagName)
    else:
        print("Did not tag " + tagName)


def update_release_notes(tagName: str):
    with open("RELEASE-NOTES-SSMV.md", "r") as file:
        lines = file.readlines()

    template_start = lines.index("<!--START TEMPLATE\n")
    template_end   = lines.index("END TEMPLATE-->\n", template_start)
    master_header  = lines.index("## Master\n", template_end)

    template_lines = lines[template_start + 1 : template_end]

    # Go through and delete "*None yet*" entries.
    i = master_header
    while i < len(lines):
        if lines[i] != "*None yet*\n":
            i += 1
            continue

        # Delete many lines around it, to remove the header and some whitespace too.
        del lines[i - 3 : i + 1]
        i -= 3

    # Replace current "master" header with the new version to tag.
    lines[master_header] = f"## {tagName}\n"

    # Insert template above newest version.
    lines[master_header : master_header] = template_lines

    with open("RELEASE-NOTES-SSMV.md", "w") as file:
        file.writelines(lines)


def undo_version(tagName: str):
    # Might want to work out some magic here to auto-identify the version from the commit
    if version == None:
        print("Version required for undo operation (try --help)")
        sys.exit(1)

    if branch == None:
        print("branch required for undo operation (try --help)")
        sys.exit(1)

    # Delete the version (good verification all by itself really)
    subprocess.run(["git", "tag", "-d", tagName], check=True)
    # Tag the commit we're about to delete because we could be deleting the wrong thing.
    savename = "version-undo-backup-" + str(int(time.time()))
    subprocess.run(["git", "tag", savename], check=True)
    # *Outright eliminate the commit from the branch!* - Dangerous if we get rid of the wrong commit, hence backup
    subprocess.run(["git", "reset", "--keep", "HEAD^"], check=True)
    print("Done (deleted commit saved as " + savename + ")")

main()
