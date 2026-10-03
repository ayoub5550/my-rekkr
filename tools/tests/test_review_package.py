"""Regression tests for packaging only; not Unity gameplay tests."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location(
    "pack", Path(__file__).resolve().parents[1] / "make_review_package.py")
pack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pack)


class ReviewPackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.root = self.base / "project"
        self.root.mkdir()
        for name in pack.REQUIRED:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("fixture\n")

    def test_allowlist_and_exclusions(self):
        for name in ["Assets/game.cs", "Assets/game.cs.meta", "tools/HeadlessTest/HeadlessTest.csproj",
                     "ThirdParty/REKKR/rekkr.zip", "docs/buyer/BUILDING.md", "LICENSE"]:
            self.assertTrue(pack.included(Path(name)), name)
        for name in [".git/config", ".env", "Assets/.env", "Assets/a.jks",
                     "tools/HeadlessTest/bin/x.dll", "tools/HeadlessTest/obj/project.assets.json",
                     "tools/__pycache__/x.pyc", "Assets/credentials.json", "AGENTS.md",
                     "docs/marketplace/DRAFT.md", "Builds/test.apk", "Assets/id_rsa",
                     "tools/key.pem", ".worktrees/x/Assets/code.cs"]:
            self.assertFalse(pack.included(Path(name)), name)

    def test_missing_notice_fails(self):
        (self.root / "LICENSE").unlink()
        with self.assertRaisesRegex(ValueError, "Missing"):
            pack.build_package(self.root, self.base / "missing.zip")

    def test_output_inside_project_fails(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            pack.build_package(self.root, self.root / "bad.zip")

    def test_symlink_fails(self):
        target = self.base / "private.txt"
        target.write_text("private")
        (self.root / "Assets" / "linked.txt").symlink_to(target)
        with self.assertRaisesRegex(ValueError, "Symlink"):
            pack.build_package(self.root, self.base / "linked.zip")

    def test_lfs_pointer_fails(self):
        with self.assertRaisesRegex(ValueError, "LFS"):
            pack.validate_data(Path("Assets/data.wad"),
                               b"version https://git-lfs.github.com/spec/v1")

    def test_private_key_fails(self):
        with self.assertRaisesRegex(ValueError, "secret"):
            pack.validate_data(Path("Assets/note.txt"),
                               b"-----BEGIN " + b"PRIVATE KEY-----")

    def test_signing_fields_fail(self):
        path = Path("ProjectSettings/ProjectSettings.asset")
        for data in [b"  AndroidKeystoreName: /private/key\n",
                     b"  androidUseCustomKeystore: 1\n",
                     b"  AndroidKeyaliasPass: sensitive\n"]:
            with self.assertRaises(ValueError):
                pack.validate_data(path, data)
        pack.validate_data(path, b"  AndroidKeystoreName: '{inproject}: '\n")

    def test_manifest_crc_determinism_and_notices(self):
        a, b = self.base / "a.zip", self.base / "b.zip"
        pack.build_package(self.root, a)
        pack.build_package(self.root, b)
        self.assertEqual(a.read_bytes(), b.read_bytes())
        with zipfile.ZipFile(a) as archive:
            self.assertIsNone(archive.testzip())
            prefix = "MyRekkrReview/"
            manifest = json.loads(archive.read(prefix + "PACKAGE_MANIFEST.json"))
            self.assertEqual(manifest["status"], "review-only")
            paths = {row["path"] for row in manifest["files"]}
            self.assertTrue(pack.REQUIRED <= paths)
            for row in manifest["files"]:
                data = archive.read(prefix + row["path"])
                self.assertEqual(len(data), row["bytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), row["sha256"])

    def test_existing_output_not_overwritten(self):
        output = self.base / "existing.zip"
        output.write_bytes(b"keep")
        with self.assertRaisesRegex(ValueError, "exists"):
            pack.build_package(self.root, output)
        self.assertEqual(output.read_bytes(), b"keep")


if __name__ == "__main__":
    unittest.main()
