#!/usr/bin/env python3
"""Run serial local checks, never GitHub Actions. See docs/local-validation.md."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def plan(scope, restore=False, native=False, system=sys.platform):
    steps = []
    if native:
        if system != "win32":
            raise ValueError("--native requires Windows")
        steps.append(("native", ["pwsh", "-NoProfile", "-File", "native/desktop-control/build.ps1"], ROOT))
    if restore:
        steps += [("restore", ["dotnet", "restore", "OmniCore.slnx"], ROOT),
                  ("sdk-install", ["npm", "ci", "--ignore-scripts", "--no-audit", "--no-fund"], ROOT / "sdk/extensions")]
    steps.append(("npm", ["npm", "test"], ROOT))
    for pattern in ("test_visual_explainer_report.py", "test_regression_*.py", "test_validate_local.py"):
        steps.append((pattern, [sys.executable, "-m", "unittest", "discover", "-s", "tests", "-p", pattern], ROOT))
    if scope == "quick":
        return steps
    steps += [("sdk", ["npm", "test"], ROOT / "sdk/extensions"),
              ("build", ["dotnet", "build", "OmniCore.slnx", "--no-restore"], ROOT),
              ("evaluation-build", ["dotnet", "build", "tools/evaluation/CoreDriver/CoreDriver.csproj"] + ([] if restore else ["--no-restore"]), ROOT)]
    command = ["dotnet", "test"]
    command += (["--project", "tests/OmniCore.Tests/OmniCore.Tests.csproj"] if scope == "tui"
                else ["--solution", "OmniCore.slnx", "--max-parallel-test-modules", "1"])
    command += ["--no-build", "--no-restore", "--parallel", "none", "--output", "Detailed", "--no-progress"]
    if scope in ("core", "tui"):
        operator = "~" if scope == "tui" else "!~"
        command += ["--filter", f"FullyQualifiedName{operator}OmniCore.Tests.TuiWiringTests"]
    if system == "linux":
        command = ["sh", "scripts/with-linux-secret-service.sh"] + command
    steps.append((scope, command, ROOT))
    return steps


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", choices=("full", "core", "tui", "quick"), default="full")
    parser.add_argument("--restore", action="store_true", help="Restore NuGet/npm dependencies (may access registries)")
    parser.add_argument("--native", action="store_true", help="Build/test Windows Rust engine; updates native hashes/notices")
    parser.add_argument("--dry-run", action="store_true", help="Print commands without executing or creating files")
    args = parser.parse_args(argv)
    steps = plan(args.scope, args.restore, args.native)
    if args.dry_run:
        for name, command, cwd in steps:
            print(f"{name}: {cwd}: {json.dumps(command)}")
        return 0
    evidence = Path(tempfile.mkdtemp(prefix="omnicore-local-validation-"))
    env = os.environ.copy()
    for key, directory in (("OMNICORE_DATA_DIR", "data"), ("OMNICORE_CONFIG_DIR", "config"),
                           ("OMNICORE_LINUX_ARTIFACTS_DIR", "linux")):
        env[key] = str(evidence / directory)
        (evidence / directory).mkdir()
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    results = {"scope": args.scope, "native_requested": args.native, "platform": sys.platform,
               "status": "running", "steps": []}
    print(f"Evidence: {evidence}", flush=True)
    try:
        for index, (name, command, cwd) in enumerate(steps):
            executable = shutil.which("npm.cmd" if os.name == "nt" and command[0] == "npm" else command[0])
            if not executable:
                raise RuntimeError(f"Missing prerequisite: {command[0]}")
            log = evidence / f"{index:02d}-{name.replace('*', 'all')}.log"
            result = {"name": name, "command": command, "cwd": str(cwd), "log": str(log)}
            results["steps"].append(result)
            print(f"Running {name}; log: {log}", flush=True)
            with log.open("w", encoding="utf-8") as output:
                completed = subprocess.run([executable, *command[1:]], cwd=cwd, env=env,
                                           stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT)
            result["exit_code"] = completed.returncode
            if completed.returncode:
                raise RuntimeError(f"{name} failed ({completed.returncode}); see {log}")
        results["status"] = "passed"
        print(f"Passed scope {args.scope}. Packaging and other platforms are not covered.", flush=True)
        return 0
    except (OSError, RuntimeError, KeyboardInterrupt) as error:
        results["status"] = "failed"
        results["error"] = str(error) or "Interrupted"
        print(results["error"], file=sys.stderr)
        return 1
    finally:
        (evidence / "summary.json").write_text(json.dumps(results, indent=2), encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
