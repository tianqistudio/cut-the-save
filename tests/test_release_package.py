"""Positive and negative checks against the actual ZIP verifier; never launch payloads."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


class PackageValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        dll = Path(os.environ.get('CTS_TEST_APP_DLL', ROOT / 'src/CutTheSave.App/bin/Release/net10.0-windows/CutTheSave.dll'))
        if not dll.is_file():
            raise RuntimeError('Build the app or set CTS_TEST_APP_DLL to the exact reviewed assembly.')
        cls.assembly = dll.read_bytes()
        cls.root = Path(tempfile.mkdtemp(prefix='CutTheSave-package-fixtures-'))
        print('FIXTURE', cls.root, flush=True)

    @classmethod
    def tearDownClass(cls):
        if os.environ.get('CTS_RETAIN_TEST_FILES') != '1':
            shutil.rmtree(cls.root)

    def verify(self, case):
        root = self.root / case
        root.mkdir()
        names = ['CutTheSave.exe','CutTheSave.dll','CutTheSave.Updater.exe','CutTheSave.deps.json','CutTheSave.runtimeconfig.json','README.md','README.en.md','docs/guide.md','docs/appearance.md','docs/rtss-safety.md','docs/write-safety.md','docs/backup-safety.md','guide.md','PRIVACY.md','SECURITY.md','THIRD_PARTY_NOTICES.md','CHANGELOG.md','RELEASE_CHECKLIST.md','FIELD_ACCEPTANCE.md','CutTheSave.update.ini.example','UPDATE_CONFIGURATION.md']
        files = {name: b'synthetic, not executable' for name in names}
        files['CutTheSave.dll'] = self.assembly
        files['release-metadata.json'] = json.dumps({'schema_version':1,'version':'999.0.0' if case=='version' else ET.parse(ROOT/'Directory.Build.props').findtext('.//ReleaseVersion'),'runtime':'win-x64'}).encode()
        if case == 'missing-main': del files['CutTheSave.exe']
        if case == 'private': files['ctr_save.bin'] = b'fake save'
        if case == 'traversal': files['../escape.txt'] = b'no'
        if case == 'duplicate-zip': files['cutthesave.EXE'] = b'duplicate'
        if case == 'collision': files['docs'] = b'file'; files['docs/x.md'] = b'child'
        package = root/'candidate.zip'
        with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as archive:
            for name,data in files.items(): archive.writestr(name,data)
        hashes = [f'{hashlib.sha256(data).hexdigest()}  {name}' for name,data in files.items()]
        hashes.append(f'{hashlib.sha256(package.read_bytes()).hexdigest()}  candidate.zip')
        if case == 'missing-hash': hashes = [line for line in hashes if not line.endswith('  CutTheSave.dll')]
        if case == 'duplicate-hash': hashes.append(hashes[0])
        if case == 'wrong-hash': hashes[0] = '0'*64 + '  CutTheSave.exe'
        checksum = root/'SHA256.txt'; checksum.write_text('\n'.join(hashes)+'\n',encoding='utf-8')
        result = subprocess.run(['pwsh','-NoProfile','-File',str(ROOT/'scripts/Verify-CutTheSaveRelease.ps1'),'-Zip',str(package),'-Checksums',str(checksum)],capture_output=True,text=True,encoding='utf-8',errors='replace')
        return result

    def test_valid_archive(self):
        result=self.verify('valid'); self.assertEqual(result.returncode,0,result.stdout+result.stderr)

    def test_invalid_archives(self):
        for case in ['missing-main','missing-hash','duplicate-hash','wrong-hash','private','traversal','duplicate-zip','collision','version']:
            with self.subTest(case=case):
                result=self.verify(case); self.assertNotEqual(result.returncode,0,result.stdout+result.stderr)


if __name__ == '__main__': unittest.main()
