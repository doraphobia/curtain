import importlib.util
import shutil
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = ROOT / "Tools" / "FontFamilySync" / "font_family_sync.py"
FONT_PATH = ROOT / "Assets" / "TextMesh Pro" / "Fonts" / "LiberationSans.ttf"

spec = importlib.util.spec_from_file_location("font_family_sync", MODULE_PATH)
sync = importlib.util.module_from_spec(spec)
assert spec.loader is not None
sys.modules["font_family_sync"] = sync
spec.loader.exec_module(sync)


class FontFamilySyncTests(unittest.TestCase):
    def test_reads_font_family_name(self):
        font = sync.read_font_file(FONT_PATH)
        self.assertIsNotNone(font)
        families = {name.lower() for name in font.family_names}
        self.assertIn("liberation sans", families)

    def test_two_device_sync_via_shared_vault(self):
        with tempfile.TemporaryDirectory(prefix="font_family_sync_test_") as tmp:
            root = Path(tmp)
            vault = root / "vault"
            device_a_fonts = root / "device-a-fonts"
            device_b_fonts = root / "device-b-fonts"
            device_a_fonts.mkdir()
            device_b_fonts.mkdir()
            shutil.copy2(FONT_PATH, device_a_fonts / FONT_PATH.name)

            config_a = sync.SyncConfig(
                vault=vault,
                device_id="device-a",
                device_name="Device A",
                publish_roots=[device_a_fonts],
                installed_roots=[device_a_fonts],
                install_root=device_a_fonts,
                ignore_patterns=[],
            )
            first = sync.sync_once(config_a)
            self.assertEqual(first["published_new_files"], 1)
            self.assertEqual(first["errors"], [])

            config_b = sync.SyncConfig(
                vault=vault,
                device_id="device-b",
                device_name="Device B",
                publish_roots=[device_b_fonts],
                installed_roots=[device_b_fonts],
                install_root=device_b_fonts,
                ignore_patterns=[],
            )
            second = sync.sync_once(config_b)
            self.assertEqual(second["installed_new_files"], 1)
            self.assertEqual(second["errors"], [])

            installed = list(device_b_fonts.glob("*.ttf"))
            self.assertEqual(len(installed), 1)
            self.assertEqual(sync.sha256_file(installed[0]), sync.sha256_file(FONT_PATH))

            status = sync.status(config_b)
            self.assertEqual(status["missing_vault_files_here"], 0)
            self.assertEqual(status["installed_vault_files_here"], 1)


if __name__ == "__main__":
    unittest.main()
