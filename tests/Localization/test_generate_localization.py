import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('localization', ROOT / 'tools/generate_localization.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class TranslationChecks(unittest.TestCase):
    def catalog(self, content):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'HapticAudioFeedback_fr-FR.xliff'
            path.write_text(content, encoding='utf-8')
            return module.catalog(path)

    def fixture(self, targets='<target>Bonjour</target>', extra=''):
        return '<xliff version="1.2"><file original="test" source-language="en-US" target-language="fr-FR"><body><trans-unit id="hello"><source>Hello</source>'+targets+'</trans-unit>'+extra+'</body></file></xliff>'

    def test_translation_and_namespace(self):
        for namespace in ['', ' xmlns="urn:oasis:names:tc:xliff:document:1.2"']:
            language, strings, _ = self.catalog(self.fixture().replace('<xliff ', '<xliff'+namespace+' '))
            self.assertEqual((language, strings), ('fr-FR', {'Hello': 'Bonjour'}))

    def test_untranslated_falls_back(self):
        for target in ['', '<target state="needs-translation"/>']:
            self.assertEqual(self.catalog(self.fixture(target))[1]['Hello'], 'Hello')

    def test_conflicting_source_rejected(self):
        extra='<trans-unit id="other"><source>Hello</source><target>Salut</target></trans-unit>'
        with self.assertRaisesRegex(ValueError, 'conflicting'):
            self.catalog(self.fixture(extra=extra))

    def test_invalid_language_and_duplicate_id(self):
        with self.assertRaises(ValueError):
            self.catalog(self.fixture().replace('fr-FR','../fr'))
        extra='<trans-unit id="hello"><source>Other</source><target>Autre</target></trans-unit>'
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            self.catalog(self.fixture(extra=extra))

    def test_spacing_preserved(self):
        _, strings, _ = self.catalog(self.fixture('<target> Bonjour </target>').replace('<source>Hello</source>', '<source> Hello </source>'))
        self.assertEqual(strings, {' Hello ': ' Bonjour '})

    def test_template_covers_sdk_and_browser(self):
        generated = module.outputs()
        root = ET.fromstring(generated[module.TEMPLATE])
        sources = {u.findtext('source') for u in root.iter('trans-unit')}
        self.assertIn('Sensitivity', sources)
        self.assertIn('Open haptic settings', sources)
        self.assertTrue(all(not u.findtext('target') for u in root.iter('trans-unit')))
        self.assertTrue(all(f.get('target-language')=='REPLACE-ME' for f in root.findall('file')))

if __name__ == '__main__':
    unittest.main()
