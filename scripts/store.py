"""Builds a mod's mod-site package and publishes it on Thunderstore (https://thunderstore.io/c/mycopunk/, where r2modman and
Gale install from).

    python scripts/store.py NoBlasphemy                 build, package and check (nothing is sent)
    python scripts/store.py NoBlasphemy --no-build      package the DLL that is already built
    python scripts/store.py NoBlasphemy --publish       ...and upload it (needs TCLI_AUTH_TOKEN). Ask the user first: a version can
                                                        never be changed or deleted, only followed by a higher one
    python scripts/store.py NoBlasphemy --update-readme write the README's "Recent changes" (the changelog's newest 3) and stop

The package is a Thunderstore-format zip (manifest.json, icon.png, README.md, CHANGELOG.md, LICENSE.md, plugins/<Mod>/...), written to
build/store/<team>-<name>-<version>.zip. Everything about a mod lives in mods/<Mod>/: store.json (team, name, description, website,
dependencies, files, categories), icon.png (256 x 256), CHANGELOG.md (a "## <version>" heading per release), LICENSE.md and README.md;
the version is its Version constant (or its [BepInPlugin] version).
The README's relative links are made absolute (raw.githubusercontent.com for images): those only show while the repo is public.
Its <!-- RECENT-CHANGES --> block must match the changelog's newest 3 versions (--update-readme writes it; a stale one stops packaging).
<!-- GITHUB-ONLY -->...<!-- END GITHUB-ONLY --> is left out on the store, <!-- STORE-ONLY: ... --> shown only there.

Hexium has no Mycopunk community (checked 2026-10-21: its API answers for any name, but its community list has no Mycopunk). If one
appears, add a "hexium" block to store.json ({"community", "categories", "install_location"}) and this publishes there too, with
HEXIUM_AUTH_TOKEN.

TCLI_AUTH_TOKEN is the Quads_Lab service account's Thunderstore token, kept in a user environment variable, never in a file.
"""
import argparse
import json
import os
import re
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent.parent
REPO = "HardHeadHackerHead/mycopunk-mods"
SITES = {
    "thunderstore": {"url": "https://thunderstore.io", "token": "TCLI_AUTH_TOKEN"},
    "hexium": {"url": "https://hexium.gg", "token": "HEXIUM_AUTH_TOKEN"},
}


def call(method, url, token=None, body=None, raw=None, site=None):
    """One request. Returns (status, parsed JSON or text, response headers)."""
    h = {"User-Agent": "quadslab-mycopunk-mods-store"}
    data = raw
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        h["Content-Type"] = "application/json"
    if token and site and urlparse(url).netloc == urlparse(site).netloc:  # the token only ever goes to the site itself
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(url, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            status, text, rh = r.status, r.read().decode("utf-8", "replace"), r.headers
    except urllib.error.HTTPError as e:
        status, text, rh = e.code, e.read().decode("utf-8", "replace"), e.headers
    except urllib.error.URLError as e:
        return 0, str(e.reason), {}
    try:
        return status, json.loads(text), rh
    except ValueError:
        return status, text, rh


def fail(msg):
    print("store: " + msg, file=sys.stderr)
    sys.exit(1)


def git(*args):
    r = subprocess.run(["git", "-C", str(ROOT), *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def token_for(site):
    """From the environment, or (a token saved with setx only reaches terminals opened after it) the user's registry value."""
    name = SITES[site]["token"]
    token = os.environ.get(name)
    if not token and os.name == "nt":
        r = subprocess.run(["powershell", "-NoProfile", "-Command", f"[Environment]::GetEnvironmentVariable('{name}','User')"],
                           capture_output=True, text=True)
        token = r.stdout.strip() or None
    return token


def read_config(mod):
    folder = ROOT / "mods" / mod
    if not (folder / "store.json").exists():
        fail(f"no {folder / 'store.json'}")
    cfg = json.loads((folder / "store.json").read_text(encoding="utf-8"))
    src = "\n".join(p.read_text(encoding="utf-8") for p in folder.glob("*.cs"))
    m = re.search(r'const string Version = "([^"]+)"', src) or re.search(r'BepInPlugin\([^)]*"(\d+\.\d+\.\d+)"\)', src)
    if not m:
        fail(f"no version in {folder}'s sources (a Version constant or [BepInPlugin(..., \"1.0.0\")])")
    cfg.update(mod=mod, version=m.group(1), readme=folder / "README.md", icon=ROOT / cfg.get("icon", f"mods/{mod}/icon.png"),
               changelog=folder / "CHANGELOG.md", license=folder / "LICENSE.md", plugin_folder=cfg.get("plugin_folder", mod),
               sources=folder, tracked=[f"mods/{mod}"])
    cfg["full"] = f"{cfg['team']}-{cfg['name']}-{cfg['version']}"
    cfg["zip"] = ROOT / "build" / "store" / f"{cfg['full']}.zip"
    cfg["sites"] = [s for s in SITES if s in cfg]
    return cfg


def vkey(v):
    return tuple(int(x) for x in v.split("."))


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


RECENT = re.compile(r"(<!-- RECENT-CHANGES[^>]*-->\n)(.*?)(<!-- END RECENT-CHANGES -->)", re.S)


def recent_changes(cfg, count=3):
    """The changelog's newest versions as one line each: '- **1.2.0** what changed'."""
    text = cfg["changelog"].read_text(encoding="utf-8") if cfg["changelog"].exists() else ""
    lines = []
    for m in re.finditer(r"^## (\S+)\s*\n(.*?)(?=^## |\Z)", text, flags=re.S | re.M):
        items = [re.sub(r"\s+", " ", i).strip() for i in re.split(r"^- ", m.group(2), flags=re.M) if i.strip()]
        lines.append(f"- **{m.group(1)}** " + " ".join(items))
        if len(lines) == count:
            break
    return "\n".join(lines) + "\n"


def update_readme(cfg):
    text = cfg["readme"].read_text(encoding="utf-8")
    if not RECENT.search(text):
        fail(f"{cfg['readme'].relative_to(ROOT)} has no <!-- RECENT-CHANGES --> block")
    new = RECENT.sub(lambda m: m.group(1) + recent_changes(cfg) + m.group(3), text)
    if new != text:
        cfg["readme"].write_text(new, encoding="utf-8")
    print(f"  {cfg['readme'].relative_to(ROOT)}: recent changes {'written' if new != text else 'already current'}")


def store_readme(cfg, branch):
    """The README as the mod site shows it: <!-- GITHUB-ONLY -->...<!-- END GITHUB-ONLY --> left out, relative links absolute."""
    text = cfg["readme"].read_text(encoding="utf-8")
    text = re.sub(r"<!-- GITHUB-ONLY -->.*?<!-- END GITHUB-ONLY -->\s*", "", text, flags=re.S)
    text = re.sub(r"<!-- STORE-ONLY:(.*?)-->", r"\1", text, flags=re.S)
    here = cfg["readme"].parent.relative_to(ROOT).as_posix() + "/"   # links are relative to the README's own folder
    raw = f"https://raw.githubusercontent.com/{REPO}/{branch}/{here}"
    blob = f"https://github.com/{REPO}/blob/{branch}/{here}"
    absolute = lambda base: (lambda m: m.group(1) + (base + m.group(2) if not re.match(r"[a-z]+:|#", m.group(2)) else m.group(2)) + m.group(3))
    text = re.sub(r'(<img[^>]*?src=")([^"]+)(")', absolute(raw), text)
    text = re.sub(r"(!\[[^\]]*\]\()([^)\s]+)(\))", absolute(raw), text)
    text = re.sub(r"((?<!!)\[[^\]]*\]\()([^)\s]+)(\))", absolute(blob), text)
    return text


def check(cfg, built_now):
    """Everything an upload needs. Returns (problems, warnings, the store README)."""
    problems, warnings = [], []
    if not re.fullmatch(r"[A-Za-z0-9_]+", cfg["name"]):
        problems.append(f"name {cfg['name']!r}: letters, digits and _ only")
    if not re.fullmatch(r"\d+\.\d+\.\d+", cfg["version"]):
        problems.append(f"version {cfg['version']} is not Major.Minor.Patch")
    if len(cfg["description"]) > 250 or "TODO" in cfg["description"]:
        problems.append(f"description: 250 characters at most, no TODO (it is {len(cfg['description'])})")
    if not cfg["sites"]:
        problems.append("store.json names no site (a \"thunderstore\" block)")
    icon = cfg["icon"]
    if not icon.exists():
        problems.append(f"no {icon.relative_to(ROOT)} (a 256 x 256 PNG)")
    elif png_size(icon) != (256, 256):
        problems.append(f"{icon.relative_to(ROOT)} is {png_size(icon)}, not a 256 x 256 PNG")
    changelog = cfg["changelog"].read_text(encoding="utf-8") if cfg["changelog"].exists() else ""
    if f"## {cfg['version']}" not in changelog:
        problems.append(f"{cfg['changelog'].relative_to(ROOT)} has no '## {cfg['version']}' heading")
    m = RECENT.search(cfg["readme"].read_text(encoding="utf-8"))
    if m and m.group(2) != recent_changes(cfg):
        problems.append(f"{cfg['readme'].relative_to(ROOT)}'s recent changes don't match the changelog: run with --update-readme")
    if not cfg["license"].exists():
        problems.append(f"no {cfg['license'].relative_to(ROOT)}: every package carries its license")
    for f in cfg["files"]:
        if not (ROOT / f).exists():
            problems.append(f"missing {f} (build first)")
    dll = ROOT / cfg["files"][0]
    if dll.exists() and not built_now:
        newest = max((p.stat().st_mtime, p) for p in cfg["sources"].rglob("*.cs") if "obj" not in p.parts)
        if newest[0] > dll.stat().st_mtime:
            problems.append(f"{newest[1].relative_to(ROOT)} is newer than the DLL: build again (drop --no-build)")
    if dll.exists() and b"AI_Assisted_Creation" not in dll.read_bytes():
        problems.append("the DLL has no AI_Assisted_Creation AssemblyMetadata (Thunderstore requires it: mods/Directory.Build.props adds it)")
    if git("status", "--porcelain", "--", *cfg["tracked"]):
        warnings.append(f"uncommitted changes in {', '.join(cfg['tracked'])}: the package may hold work that isn't in git")
    readme = store_readme(cfg, git("rev-parse", "--abbrev-ref", "HEAD") or "main")
    if not re.search(r"Claude|AI", readme):
        warnings.append("the README doesn't say AI helped make the mod (add 'Made with the help of Claude (Anthropic).')")
    # the README's pictures must load for anyone: pushed, and the repo public
    broken = []
    for url in sorted(set(re.findall(r'https://raw\.githubusercontent\.com/[^\s")]+', readme))):
        s, _, _ = call("HEAD", url)
        if s != 200:
            broken.append(url.rsplit("/", 1)[-1])
    if broken:
        problems.append(f"{len(broken)} README image(s) don't load publicly ({', '.join(broken[:4])}): push them first")
    # the version must be new on each site and higher than what is there
    for site in cfg["sites"]:
        s, r, _ = call("GET", f"{SITES[site]['url']}/api/experimental/package/{cfg['team']}/{cfg['name']}/")
        if s == 200 and isinstance(r, dict) and r.get("latest"):
            latest = r["latest"]["version_number"]
            if vkey(cfg["version"]) <= vkey(latest):
                problems.append(f"{site} already has {latest}: raise the version above it")
    return problems, warnings, readme


def package(cfg, readme):
    manifest = {"name": cfg["name"], "version_number": cfg["version"], "website_url": cfg["website"],
                "description": cfg["description"], "dependencies": cfg["dependencies"]}
    folder = "plugins/" + cfg["plugin_folder"] + "/"
    cfg["zip"].parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(cfg["zip"], "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("manifest.json", json.dumps(manifest, indent=2, ensure_ascii=False))
        z.writestr("README.md", readme)
        z.write(cfg["icon"], "icon.png")
        z.write(cfg["changelog"], "CHANGELOG.md")
        z.write(cfg["license"], "LICENSE.md")
        z.write(cfg["license"], folder + "LICENSE.md")   # (also next to the DLL: mod managers install only plugins/)
        for f in cfg["files"]:
            z.write(ROOT / f, folder + Path(f).name)


def upload(cfg, site, token):
    """Thunderstore's upload protocol (Hexium uses the same): the zip in parts, then the submission."""
    base, data, opts = SITES[site]["url"], cfg["zip"].read_bytes(), cfg[site]
    community = opts["community"]
    for attempt in range(15):
        s, r, _ = call("POST", f"{base}/api/experimental/usermedia/initiate-upload/", token,
                       {"filename": cfg["zip"].name, "file_size_bytes": len(data)}, site=base)
        if s not in (200, 201):
            fail(f"{site} initiate-upload: {s} {r}")
        uuid, parts = r["user_media"]["uuid"], []
        for part in r["upload_urls"]:
            chunk = data[part["offset"]:part["offset"] + part["length"]]
            s, body, rh = call("PUT", part["url"], raw=chunk)
            if s not in (200, 201, 204) or not rh.get("ETag"):
                call("POST", f"{base}/api/experimental/usermedia/{uuid}/abort-upload/", token, {}, site=base)
                fail(f"{site} upload part {part['part_number']}: {s} {body}")
            parts.append({"ETag": rh["ETag"], "PartNumber": part["part_number"]})
        s, r, _ = call("POST", f"{base}/api/experimental/usermedia/{uuid}/finish-upload/", token, {"parts": parts}, site=base)
        if s not in (200, 201):
            fail(f"{site} finish-upload: {s} {r}")
        s, r, _ = call("POST", f"{base}/api/experimental/submission/submit/", token, {
            "upload_uuid": uuid, "author_name": cfg["team"], "communities": [community], "has_nsfw_content": False,
            "categories": [], "community_categories": {community: opts["categories"]}}, site=base)
        if s in (200, 201):
            return r
        if "too many" not in json.dumps(r):  # (an upload limit: wait and send it again; a refused upload is used up)
            break
        print(f"   {site}'s upload limit: trying again in a minute ({time.strftime('%H:%M')})", flush=True)
        time.sleep(60)
    fail(f"{site} submit: {s} {r}")


def main():
    ap = argparse.ArgumentParser(description="Package a mod for Thunderstore and publish it")
    ap.add_argument("mod", help="the mod's folder name under mods/, e.g. NoBlasphemy")
    ap.add_argument("--publish", action="store_true", help="upload it (otherwise only builds and checks)")
    ap.add_argument("--no-build", action="store_true", help="use the DLL that is already built")
    ap.add_argument("--update-readme", action="store_true", help="write the README's recent changes from the changelog, then stop")
    a = ap.parse_args()
    cfg = read_config(a.mod)
    if a.update_readme:
        update_readme(cfg)
        return
    tokens = {}
    if a.publish:
        for site in cfg["sites"]:
            tokens[site] = token_for(site)
            if not tokens[site]:
                fail(f"set {SITES[site]['token']} first (in your own terminal, never in a file)")
    if not a.no_build:
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "scripts/build.ps1"), "-Mod", a.mod])
        if r.returncode != 0:
            fail("build failed")
    where = ", ".join(f"{s}: {cfg[s]['community']} ({', '.join(cfg[s]['categories'])})" for s in cfg["sites"])
    print(f"== {cfg['full']}  ({where})")
    problems, warnings, readme = check(cfg, built_now=not a.no_build)
    for w in warnings:
        print("  warning: " + w)
    for p in problems:
        print("  PROBLEM: " + p)
    if problems:
        fail(f"{len(problems)} problem(s): nothing packaged")
    package(cfg, readme)
    print(f"  ready: {cfg['zip'].relative_to(ROOT)} ({cfg['zip'].stat().st_size // 1024} KB)")
    if not a.publish:
        print("  test it: r2modman or Gale > a new, empty Mycopunk profile > import local mod > that zip, then play")
        return
    for site in cfg["sites"]:
        upload(cfg, site, tokens[site])
        if site == "thunderstore":
            print(f"  uploaded: https://thunderstore.io/c/{cfg[site]['community']}/p/{cfg['team']}/{cfg['name']}/")
        else:
            s, r, _ = call("POST", f"{SITES[site]['url']}/api/mod/{cfg['team']}/{cfg['name']}/set-metadata", tokens[site],
                           {"install_location": cfg[site]["install_location"], "categories": cfg[site]["categories"]}, site=SITES[site]["url"])
            print(f"  uploaded on {site}; metadata: {s} {r}")


if __name__ == "__main__":
    main()
