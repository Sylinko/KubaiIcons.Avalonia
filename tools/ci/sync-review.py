"""Summarize an upstream update relative to the target branch, including shared artwork changes."""
import json
import re
import subprocess
import sys
from pathlib import Path
from xml.etree import ElementTree as ET


def git(*args):
    return subprocess.check_output(["git", *args], encoding="utf-8")


def previous(path):
    return git("show", f"{base}:{path}")


def identities(text):
    return {row.attrib["Include"]: row.attrib for row in ET.fromstring(text).iter("KubaiId")}


def placements(text):
    result = {}
    for line in text.splitlines():
        match = re.match(r"\s*KubaiIconKind\.(\w+)( => new\(.*\)|),?$", line)
        if match:
            result.setdefault(match[1], []).append(line.strip())
    return result


def motions(text):
    return {row["name"]: row for row in json.loads(text)["icons"]}


base = sys.argv[1]
out = Path("artifacts/review")
out.mkdir(parents=True, exist_ok=True)
ledger = "catalog/ids.props"
old, new = identities(previous(ledger)), identities(Path(ledger).read_text(encoding="utf-8"))
added, backfilled = [], []
changed = {"Layered": set(), "Duotone": set()}
for kind, row in new.items():
    if kind == "None":
        continue
    slug = row["Source"]
    families = [family for family in changed if row.get(family) == "true"]
    if kind not in old:
        added.append(f"{kind} = {row['Value']} ({', '.join(families)})")
    for family in families:
        if old.get(kind, {}).get(family) != "true":
            changed[family].add(slug)
            if kind in old:
                backfilled.append(f"{kind} = {row['Value']} (+{family})")

dirty = set(git("diff", "--name-only", base, "--").splitlines())
root = Path("src/KubaiIcons.Avalonia/Art")
blocks = lambda folder: {Path(p).stem for p in dirty if p.startswith(f"{root.as_posix()}/{folder}/")}
scenes, badges = blocks("Scenes"), blocks("Badges")
changed["Duotone"].update(blocks("Duotone"))
manifest = json.loads(Path("artifacts/studio-metadata/layered-icons.json").read_bytes())
for icon in manifest:
    if (icon.get("base", icon["name"]) in scenes or icon.get("badge") in badges
            or (icon.get("badge") == "off" and "overlay-slash" in scenes)):
        changed["Layered"].add(icon["name"])
code = "src/KubaiIcons.Avalonia/Rendering/CompositionData.g.cs"
if code in dirty:
    before, after = placements(previous(code)), placements(Path(code).read_text(encoding="utf-8"))
    changed["Layered"].update(new[k]["Source"] for k in before.keys() | after.keys() if before.get(k) != after.get(k))
motion = "tests/KubaiIcons.Tests/Contracts/duotone-motion.json"
if motion in dirty:
    before, after = motions(previous(motion)), motions(Path(motion).read_bytes())
    changed["Duotone"].update(name for name in after if before.get(name) != after[name])

review = {family: sorted(names) for family, names in changed.items()}
(out / "changed.json").write_text(json.dumps(review, indent=2) + "\n", encoding="utf-8")
summary = []
for title, names in [("New IDs", added), ("Existing IDs gaining a style", backfilled),
                     *[(f"{family} artwork to review", names) for family, names in review.items()]]:
    summary.append(f"**{title} ({len(names)})**: " + (", ".join(f"`{n}`" for n in names[:16]) or "none")
                   + (" …" if len(names) > 16 else ""))
notice = "licenses/LICENSE-ICONS" in dirty
summary.append("**Upstream artwork license:** " + ("changed — review before merging." if notice else "unchanged."))
summary.append("Open the Windows Check artifact **upstream-artwork-comparison** and its `index.html` for the changed poses.")
(out / "summary.md").write_text("\n\n".join(summary) + "\n", encoding="utf-8")
