"""Check the staged proposal without checking it out or executing candidate scripts."""
import json
import re
import subprocess
import sys

files = {
    "catalog/upstream.json", "catalog/ids.props", "catalog/layered-bases.json",
    "catalog/layered-labels.json", "catalog/layered-compositions.json",
    "src/KubaiIcons.Avalonia/CatalogData.g.cs", "src/KubaiIcons.Avalonia/Rendering/CompositionData.g.cs",
    "licenses/LICENSE-ICONS", "samples/IconStudio/Catalog/catalog.json",
    "tests/KubaiIcons.Tests/Contracts/duotone-motion.json", "tests/KubaiIcons.Tests/Contracts/published-inventory.json",
}
art = re.compile(r"src/KubaiIcons\.Avalonia/Art/(Badges|Scenes|Duotone)/[a-z0-9-]+\.kbi")
rows = subprocess.check_output(["git", "diff", "--cached", "--raw", "--no-renames"], text=True)
for row in rows.splitlines():
    header, name = row.split("\t", 1)
    if header.split()[1] != "100644" or (name not in files and not art.fullmatch(name)):
        sys.exit("Unexpected generated patch entry: " + name)
pin = json.loads(subprocess.check_output(["git", "show", ":catalog/upstream.json"]))
if pin["version"] != "kubai-icons@" + sys.argv[1]:
    sys.exit("Patch does not contain the requested upstream version")
