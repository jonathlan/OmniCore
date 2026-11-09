import importlib.util
from pathlib import Path
import unittest
from unittest import mock
import json
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("validate_local", ROOT / "scripts/validate-local.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class LocalValidationTests(unittest.TestCase):
    def test_full_is_serial_and_unfiltered(self):
        command = runner.plan("full", system="win32")[-1][1]
        self.assertIn("--solution", command)
        self.assertNotIn("--filter", command)
        self.assertEqual(command[command.index("--parallel") + 1], "none")
        self.assertEqual(command[command.index("--max-parallel-test-modules") + 1], "1")

    def test_scopes_are_complementary(self):
        core = runner.plan("core", system="win32")[-1][1][-1]
        tui = runner.plan("tui", system="win32")[-1][1][-1]
        self.assertEqual(core.replace("!~", "~"), tui)

    def test_linux_uses_private_secret_service(self):
        self.assertEqual(runner.plan("full", system="linux")[-1][1][:2],
                         ["sh", "scripts/with-linux-secret-service.sh"])

    def test_quick_has_no_build_or_restore(self):
        steps = runner.plan("quick")
        self.assertFalse(any(command[0] in ("dotnet", "pwsh") for _, command, _ in steps))

    def test_native_and_restore_are_opt_in(self):
        default = [name for name, _, _ in runner.plan("full", system="win32")]
        self.assertNotIn("restore", default)
        self.assertNotIn("native", default)
        explicit = [name for name, _, _ in runner.plan("full", True, True, "win32")]
        self.assertIn("native", explicit)
        self.assertIn("restore", explicit)
        self.assertIn("sdk-install", explicit)

    def test_dry_run_does_not_launch_commands_or_create_evidence(self):
        with mock.patch.object(runner.subprocess, "run") as run, mock.patch.object(runner.tempfile, "mkdtemp") as temporary:
            self.assertEqual(runner.main(["--dry-run", "--scope", "quick"]), 0)
            run.assert_not_called()
            temporary.assert_not_called()

    def test_ci_has_manual_trigger_only(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        triggers = workflow.split("\non:\n", 1)[1].split("\npermissions:", 1)[0]
        declarations = [line.strip() for line in triggers.splitlines()
                        if line.strip() and not line.lstrip().startswith("#")]
        self.assertEqual(declarations, ["workflow_dispatch:"])

    def test_failure_stops_and_preserves_isolated_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            with mock.patch.object(runner.tempfile, "mkdtemp", return_value=directory), \
                 mock.patch.object(runner.shutil, "which", return_value="fake-command"), \
                 mock.patch.object(runner.subprocess, "run", return_value=subprocess.CompletedProcess([], 9)) as run:
                self.assertEqual(runner.main(["--scope", "quick"]), 1)
            self.assertEqual(run.call_count, 1)
            self.assertEqual(run.call_args.kwargs["stdin"], subprocess.DEVNULL)
            self.assertEqual(run.call_args.kwargs["env"]["OMNICORE_DATA_DIR"], str(Path(directory) / "data"))
            summary = json.loads((Path(directory) / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(summary["status"], "failed")
            self.assertEqual(summary["steps"][0]["exit_code"], 9)


if __name__ == "__main__":
    unittest.main()
