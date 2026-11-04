import unittest
from projectide_navigation import allowed

class NavigationOwnershipTests(unittest.TestCase):
    def test_product_rejects_independent_tests_and_workflows(self):
        for path in ['frontend/src/app/features/projectide/hidden.spec.ts','tests/Coglatas.Tests/ProjectIde/Hidden.cs','.github/workflows/projectide-navigation.yml']:
            self.assertFalse(allowed(path,True),path)
    def test_test_rejects_product_compiler_and_consumer_sources(self):
        for path in ['src/Coglatas.Domain/ProjectIde/CompilerBinding.cs','frontend/src/app/features/projectide/projectide-workbench.store.ts','tools/ProjectIde.Workbench.Synthetic/SyntheticWorkbenchViewModel.cs']:
            self.assertFalse(allowed(path,False),path)

if __name__ == '__main__': unittest.main()
