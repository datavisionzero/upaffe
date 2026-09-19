"""Exercise release resume and mismatch boundaries without calling GitHub."""

import hashlib
import importlib.util
import json
import os
import pathlib
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "scripts/publish-release.py"
spec = importlib.util.spec_from_file_location("publish_release", SCRIPT)
assert spec and spec.loader
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PublishReleaseTests(unittest.TestCase):
    def test_draft_completes_and_rerun_verifies_without_replacing_assets(self) -> None:
        revision = "a" * 40
        image_digest = "sha256:" + "b" * 64
        state: dict = {}
        calls: list[str] = []

        def fake_gh(*args: str) -> str:
            calls.append(" ".join(args[:2]))
            if args[0] == "api":
                return json.dumps([state] if state else [])
            if args[:2] == ("release", "create"):
                state.update(tag_name="v0.1.0", draft=True, assets=[],
                             body=pathlib.Path(args[args.index("--notes-file") + 1]).read_text())
            elif args[:2] == ("release", "upload"):
                path = pathlib.Path(args[3])
                state["assets"].append({"name": path.name, "state": "uploaded",
                                        "digest": "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()})
            elif args[:2] == ("release", "edit"):
                state["draft"] = False
            else:
                self.fail(f"Unexpected gh operation: {args}")
            return ""

        with tempfile.TemporaryDirectory() as temporary, \
             mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": "datavisionzero/upaffe"}), \
             mock.patch.object(publisher, "gh", fake_gh):
            root = pathlib.Path(temporary)
            notes = root / "notes.md"
            notes.write_text("Reviewed release notes.\n", encoding="utf-8")

            def bundle(name: str) -> pathlib.Path:
                directory = root / name
                directory.mkdir()
                entries = []
                for platform in publisher.PLATFORMS:
                    archive = directory / f"ua_0.1.0_{platform}.zip"
                    archive.write_bytes(platform.encode())
                    entries.append(f"{hashlib.sha256(archive.read_bytes()).hexdigest()}  {archive.name}")
                (directory / "SHA256SUMS").write_text("\n".join(entries) + "\n", encoding="ascii")
                (directory / "source-revision.txt").write_text(revision + "\n", encoding="ascii")
                return directory

            with mock.patch.object(sys, "argv", [str(SCRIPT), "0.1.0", revision, image_digest,
                                                str(bundle("first")), str(notes)]):
                publisher.main()
            self.assertFalse(state["draft"])
            self.assertEqual(len(state["assets"]), 7)
            self.assertIn(image_digest, state["body"])
            uploads = calls.count("release upload")

            with mock.patch.object(sys, "argv", [str(SCRIPT), "0.1.0", revision, image_digest,
                                                str(bundle("second")), str(notes)]):
                publisher.main()
            self.assertEqual(calls.count("release upload"), uploads)

            state["assets"][0]["digest"] = "sha256:" + "0" * 64
            with mock.patch.object(sys, "argv", [str(SCRIPT), "0.1.0", revision, image_digest,
                                                str(bundle("third")), str(notes)]):
                with self.assertRaisesRegex(SystemExit, "Existing release asset differs"):
                    publisher.main()


if __name__ == "__main__":
    unittest.main()
