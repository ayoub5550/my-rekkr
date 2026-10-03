"""Exercise wrapper configuration using a stub, not a Unity build."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipUnless(shutil.which("sh") and shutil.which("timeout"),
                     "requires POSIX shell and timeout")
class AndroidBuildWrapperTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for rel in ["tools/sandbox/build_android.sh", "Assets/Rekkr/Scripts/RekkrApp.cs"]:
            out = self.root / rel
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / rel, out)
        stub = self.root / "unity-stub.sh"
        stub.write_text(
            '#!/bin/sh\n'
            'printf "%s\\n%s\\n" "$REKKR_VERSION_CODE" "$REKKR_OUT" > observed.txt\n'
            'printf "[RekkrBuild] result=Succeeded\\n" > Logs/build.log\n'
        )
        stub.chmod(0o755)
        self.env = {k: v for k, v in os.environ.items() if not k.startswith("REKKR_")}
        self.env.update({
            "UNITY": str(stub), "REKKR_KEYSTORE": str(self.root / "not-a-real-key.jks"),
            "REKKR_KEYSTORE_PASS": "fixture-only",
        })

    def run_wrapper(self):
        return subprocess.run(
            ["sh", str(self.root / "tools/sandbox/build_android.sh")],
            cwd=self.root, env=self.env, capture_output=True, text=True,
        )

    def test_current_version_defaults(self):
        result = self.run_wrapper()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.root / "observed.txt").read_text().splitlines(),
                         ["8", "Builds/REKKR-0.8.0.apk"])

    def test_explicit_overrides(self):
        self.env.update(REKKR_VERSION_CODE="42", REKKR_OUT="Builds/custom.apk")
        result = self.run_wrapper()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.root / "observed.txt").read_text().splitlines(),
                         ["42", "Builds/custom.apk"])

    def test_invalid_version_code_blocks_build(self):
        for code in ("bad", "0", "-2"):
            self.env["REKKR_VERSION_CODE"] = code
            self.assertNotEqual(self.run_wrapper().returncode, 0)
            self.assertFalse((self.root / "observed.txt").exists())

    def test_unreadable_version_blocks_build(self):
        (self.root / "Assets/Rekkr/Scripts/RekkrApp.cs").write_text("// no version\n")
        self.assertNotEqual(self.run_wrapper().returncode, 0)
        self.assertFalse((self.root / "observed.txt").exists())

    def test_missing_signing_variable_blocks_build(self):
        del self.env["REKKR_KEYSTORE_PASS"]
        self.assertNotEqual(self.run_wrapper().returncode, 0)
        self.assertFalse((self.root / "observed.txt").exists())


if __name__ == "__main__":
    unittest.main()
