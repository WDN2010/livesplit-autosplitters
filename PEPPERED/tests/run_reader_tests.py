#!/usr/bin/env python3
import os
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "ReadOnlyReader.cs")
TEST = os.path.join(ROOT, "tests", "ReaderTests.cs")
BUILD = os.path.join(ROOT, "tests", ".testbuild")
DLL = os.path.join(BUILD, "Peppered.Reader.dll")
EXE = os.path.join(BUILD, "ReaderTests.exe")


def run(cmd):
    # Keep captured receipts deterministic even when stdout is block-buffered.
    print("$ " + " ".join(cmd), flush=True)
    return subprocess.run(cmd, cwd=ROOT, check=False).returncode


def main():
    for tool in ("mcs", "mono"):
        if shutil.which(tool) is None:
            print("missing required tool: " + tool, file=sys.stderr)
            return 2
    os.makedirs(BUILD, exist_ok=True)
    rc = run(["mcs", "-target:library", "-langversion:4", "-out:" + DLL, SRC])
    if rc:
        return rc
    # Compile the test fixture with the source as one assembly so it can use the
    # deliberately internal memory injection point without exposing test APIs.
    rc = run(["mcs", "-langversion:4", "-out:" + EXE, SRC, TEST])
    if rc:
        return rc
    rc = run(["mono", EXE])
    print("receipt: Peppered.Reader.dll -> " + DLL)
    print("receipt: ReaderTests.exe -> " + EXE)
    print("receipt: exit=" + str(rc))
    return rc


if __name__ == "__main__":
    sys.exit(main())
