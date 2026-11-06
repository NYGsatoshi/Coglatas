"""Bounded negative controls for documentation, state and preservation admission."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('recovery_contract', HERE / 'validate-contract.py')
guard = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(guard)


class RecoveryContractTests(unittest.TestCase):
    def folder(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        target = Path(directory.name)
        for source in HERE.iterdir():
            if source.is_file():
                shutil.copyfile(source, target / source.name)
        return target

    def mutate_json(self, folder, name, change):
        path = folder / name
        value = json.loads(path.read_text(encoding='utf-8'))
        change(value)
        path.write_text(json.dumps(value), encoding='utf-8')

    def test_original_contract_and_all_source_preservation_anchors_pass(self):
        guard.validate_contract(verify_preservation=True)

    def test_missing_decision_and_altered_approved_wording_are_rejected(self):
        for original, replacement in [('### D09', '### OMITTED'),
                                      ('Counts alone are insufficient.', 'Counts alone are sufficient.')]:
            with self.subTest(original=original):
                folder = self.folder()
                path = folder / 'approved-decisions.md'
                path.write_text(path.read_text(encoding='utf-8').replace(original, replacement), encoding='utf-8')
                with self.assertRaises(guard.ContractError):
                    guard.validate_contract(folder)

    def test_missing_deliverable_rejection_or_exit_requirement_is_rejected(self):
        for original in ['### N03', '| R07 |', '12. No Main modifications occurred.']:
            with self.subTest(original=original):
                folder = self.folder()
                path = folder / 'pr-n-contract.md'
                path.write_text(path.read_text(encoding='utf-8').replace(original, 'OMITTED'), encoding='utf-8')
                with self.assertRaises(guard.ContractError):
                    guard.validate_contract(folder)

    def test_every_unauthorized_authority_bit_is_rejected(self):
        for field in ['mainModificationAuthorized', 'governanceSettingsChangeAuthorized',
                      'requiredCheckPolicyChangeAuthorized', 'releaseApprovalGranted',
                      'automaticSuccessImplications', 'finalEvidenceVerifierImplemented']:
            with self.subTest(field=field):
                folder = self.folder()
                self.mutate_json(folder, 'program-contract.json', lambda v: v.update({field: True}))
                with self.assertRaises(guard.ContractError):
                    guard.validate_contract(folder)

    def test_each_state_is_required_and_cannot_inherit_success(self):
        for state in guard.STATES:
            for mutation in ['missing', 'complete']:
                with self.subTest(state=state, mutation=mutation):
                    folder = self.folder()
                    def change(value):
                        if mutation == 'missing':
                            del value['completionStates'][state]
                        else:
                            value['completionStates'][state]['complete'] = True
                    self.mutate_json(folder, 'program-status.json', change)
                    with self.assertRaises(guard.ContractError):
                        guard.validate_contract(folder)

    def test_mandatory_hold_cannot_disappear(self):
        folder = self.folder()
        self.mutate_json(folder, 'program-status.json', lambda v: v['holds'].pop())
        with self.assertRaises(guard.ContractError):
            guard.validate_contract(folder)

    def test_checkpoint_false_values_cannot_be_coerced_to_success(self):
        for key, value in [('MAIN_UNCHANGED', 1), ('INDEPENDENT_REVIEW_COMPLETE', 'false'),
                           ('PROGRAM_STATUS', 'COMPLETE'), ('GOVERNANCE_IDENTITY_DRIFT', 'RESOLVED')]:
            with self.subTest(key=key):
                folder = self.folder()
                self.mutate_json(folder, 'program-status.json', lambda v: v.update({key: value}))
                with self.assertRaises(guard.ContractError):
                    guard.validate_contract(folder)

    def test_missing_reference_duplicate_keys_empty_and_nonfinite_json_are_rejected(self):
        for content in ['', '{"schemaVersion":1,"schemaVersion":1}', '{"schemaVersion":NaN}', '{']:
            with self.subTest(content=content):
                folder = self.folder()
                (folder / 'program-contract.json').write_text(content, encoding='utf-8')
                with self.assertRaises(guard.ContractError):
                    guard.validate_contract(folder)
        folder = self.folder()
        (folder / 'approved-decisions.md').unlink()
        with self.assertRaises(guard.ContractError):
            guard.validate_contract(folder)

    def test_unknown_contract_fields_wrong_branch_and_missing_state_names_are_rejected(self):
        for change in [lambda v: v.update(unknown=True),
                       lambda v: v.update(integrationBranch='main'),
                       lambda v: v['completionStateNames'].pop()]:
            folder = self.folder()
            self.mutate_json(folder, 'program-contract.json', change)
            with self.assertRaises(guard.ContractError):
                guard.validate_contract(folder)

    def test_missing_ledger_identity_and_corrupt_preservation_digest_are_rejected(self):
        folder = self.folder()
        self.mutate_json(folder, 'evidence-preservation.json',
                         lambda v: v['originalLedger'].update(entryCount=44))
        with self.assertRaises(guard.ContractError):
            guard.validate_contract(folder)
        folder = self.folder()
        self.mutate_json(folder, 'evidence-preservation.json',
                         lambda v: v['anchors'][0].update(canonicalBlobSha256='0' * 64))
        with self.assertRaises(guard.ContractError):
            guard.validate_contract(folder, verify_preservation=True)

    def test_outside_local_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory) / 'scope'
            folder.mkdir()
            (folder.parent / 'outside.md').write_text('outside', encoding='utf-8')
            with self.assertRaises(guard.ContractError):
                guard.local_file(folder, '../outside.md')


if __name__ == '__main__':
    unittest.main()
