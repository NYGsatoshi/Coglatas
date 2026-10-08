"""Import the existing authenticated probe without copying or changing request semantics."""
import importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location('canonical_db_probe', Path(__file__).with_name('db-probe.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
login, request = module.login, module.request
