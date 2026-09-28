#!/usr/bin/env python3
"""
Watch the API specifications our connectors are written against.

Some venues publish a machine-readable description of their API - OpenAPI,
AsyncAPI, protobuf, an SBE schema, a Postman collection - and change it without
telling anyone. This fetches those, reduces each to the shape a connector cares
about, and compares that with the shape recorded last time. An endpoint that
appeared, a field that changed type, a message that went away: all of it shows
up here instead of in a support ticket.

What is stored is the shape, not the specification. A snapshot is a sorted list
of operations with a hash of each one's parameters, which is small enough to
read in a diff and to keep under version control; mirroring half a megabyte of
somebody else's YAML would be neither.

    python tools/spec_watch.py check                 # what changed since last time
    python tools/spec_watch.py check --only deribit
    python tools/spec_watch.py update                # accept the current shapes
    python tools/spec_watch.py list                  # what is watched, and how

`check` exits 1 when anything changed and 2 when a source could not be reached,
so it is usable from a scheduled job. Nothing outside this repository is
written: snapshots live next to this script.

Adding a venue means adding an entry to specs.json. Nothing here is specific to
any one of them beyond the format name.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - reported at run time, not a crash
    yaml = None


HERE = Path(__file__).resolve().parent
MANIFEST = HERE / "specs.json"
SNAPSHOTS = HERE / "spec-snapshots"

USER_AGENT = "StockSharp-spec-watch/1.0 (+connector maintenance)"
DEFAULT_TIMEOUT = 60


# --------------------------------------------------------------------------- #
# fetching
# --------------------------------------------------------------------------- #

def fetch(url: str, timeout: int) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return response.read()


def parse_document(raw: bytes, url: str):
    """JSON when it parses as JSON, YAML otherwise; both appear behind .yaml."""
    text = raw.decode("utf-8", "replace")
    try:
        return json.loads(text)
    except ValueError:
        pass
    if yaml is None:
        raise RuntimeError("%s is YAML and PyYAML is not installed" % url)
    return yaml.safe_load(text)


# --------------------------------------------------------------------------- #
# shapes
#
# Every extractor returns {key: fingerprint}. The key names one thing a
# connector can call or decode; the fingerprint covers everything about it that
# would make a connector wrong if it changed.
# --------------------------------------------------------------------------- #

def digest(value) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, ensure_ascii=False, default=str).encode("utf-8")
    ).hexdigest()[:16]


METHODS = ("get", "put", "post", "delete", "patch", "head", "options", "trace")


def shape_openapi(doc, _raw) -> dict[str, str]:
    out = {}
    for path, item in (doc.get("paths") or {}).items():
        if not isinstance(item, dict):
            continue
        shared = item.get("parameters") or []
        for method in METHODS:
            op = item.get(method)
            if not isinstance(op, dict):
                continue
            params = []
            for p in list(shared) + list(op.get("parameters") or []):
                if not isinstance(p, dict):
                    continue
                schema = p.get("schema") or {}
                params.append({
                    "name": p.get("name"),
                    "in": p.get("in"),
                    "required": bool(p.get("required")),
                    "type": schema.get("type") or schema.get("$ref"),
                })
            params.sort(key=lambda p: (str(p["in"]), str(p["name"])))
            body = op.get("requestBody") or {}
            content = sorted((body.get("content") or {}).keys())
            out["%s %s" % (method.upper(), path)] = digest({
                "params": params,
                "body": content,
                "required_body": bool(body.get("required")),
                "responses": sorted(str(c) for c in (op.get("responses") or {}).keys()),
            })
    return out


def shape_asyncapi(doc, _raw) -> dict[str, str]:
    out = {}
    for channel, item in (doc.get("channels") or {}).items():
        if not isinstance(item, dict):
            out["channel %s" % channel] = digest(item)
            continue
        messages = item.get("messages")
        if isinstance(messages, dict):
            names = sorted(messages.keys())
        else:
            names = sorted(
                k for k in item.keys() if k in ("subscribe", "publish", "send", "receive")
            )
        out["channel %s" % channel] = digest({"messages": names, "address": item.get("address")})
    for name, op in (doc.get("operations") or {}).items():
        if isinstance(op, dict):
            out["operation %s" % name] = digest({
                "action": op.get("action"),
                "channel": (op.get("channel") or {}).get("$ref"),
            })
    return out


SBE_MESSAGE = re.compile(rb'<sbe:message\s+[^>]*name="([^"]+)"[^>]*id="(\d+)"', re.I)
SBE_ANY_TAG = re.compile(
    rb'<(field|data|group|enum|composite|type|set)\s+([^>]*?)/?>', re.I)
SBE_ATTR = re.compile(rb'(\w+)="([^"]*)"')


def shape_sbe_xml(_doc, raw: bytes) -> dict[str, str]:
    """One entry per SBE message, fingerprinting the fields it carries."""
    out = {}
    chunks = raw.split(b"<sbe:message")
    for chunk in chunks[1:]:
        head = b"<sbe:message" + chunk
        m = SBE_MESSAGE.search(head)
        if not m:
            continue
        name = m.group(1).decode("utf-8", "replace")
        body = head.split(b"</sbe:message>", 1)[0]
        members = []
        for tag, attrs in SBE_ANY_TAG.findall(body):
            found = dict(SBE_ATTR.findall(attrs))
            members.append({
                "tag": tag.decode(),
                "name": found.get(b"name", b"").decode("utf-8", "replace"),
                "id": found.get(b"id", b"").decode(),
                "type": found.get(b"type", b"").decode("utf-8", "replace"),
            })
        members.sort(key=lambda f: (f["tag"], f["name"], f["id"]))
        out["message %s (id %s)" % (name, m.group(2).decode())] = digest(members)
    return out


FIX_MESSAGE = re.compile(
    rb"<message\s+([^>]*?)>(.*?)</message>", re.I | re.S)
FIX_MEMBER = re.compile(rb"<(field|group|component)\s+([^>]*?)/?>", re.I)
XML_ATTR = re.compile(rb"""(\w+)\s*=\s*['"]([^'"]*)['"]""")


def _attrs(blob: bytes) -> dict[str, str]:
    return {k.decode(): v.decode("utf-8", "replace") for k, v in XML_ATTR.findall(blob)}


def shape_fix_xml(_doc, raw: bytes) -> dict[str, str]:
    """
    A QuickFIX data dictionary: one entry per message, fingerprinting its fields.

    Separate from the SBE reader because the two share nothing but the angle
    brackets - different element names, and attributes quoted with apostrophes.
    """
    out = {}
    for section in (b"header", b"trailer"):
        block = re.search(b"<%s>(.*?)</%s>" % (section, section), raw, re.I | re.S)
        if block:
            members = sorted(
                "%s %s" % (a.get("name", ""), a.get("required", ""))
                for _, attrs in FIX_MEMBER.findall(block.group(1))
                for a in [_attrs(attrs)]
            )
            out["%s" % section.decode()] = digest(members)

    for attrs, body in FIX_MESSAGE.findall(raw):
        head = _attrs(attrs)
        members = sorted(
            "%s %s %s" % (tag.decode().lower(), a.get("name", ""), a.get("required", ""))
            for tag, member in FIX_MEMBER.findall(body)
            for a in [_attrs(member)]
        )
        out["message %s (msgtype %s)" % (head.get("name", "?"), head.get("msgtype", "?"))] = digest(members)
    return out


def shape_postman(doc, _raw) -> dict[str, str]:
    """One entry per request in the collection, wherever it sits in the tree."""
    out = {}

    def walk(items):
        for item in items or []:
            if not isinstance(item, dict):
                continue
            if "item" in item:
                walk(item["item"])
                continue
            request = item.get("request")
            if not isinstance(request, dict):
                continue
            url = request.get("url")
            if isinstance(url, dict):
                raw_url = url.get("raw") or "/".join(url.get("path") or [])
                query = sorted(
                    str(q.get("key")) for q in (url.get("query") or []) if isinstance(q, dict)
                )
            else:
                raw_url, query = str(url), []
            out["%s %s" % (request.get("method") or "?", raw_url)] = digest(query)

    walk(doc.get("item"))
    return out


PROTO_BLOCK = re.compile(r"^(message|enum|service)\s+(\w+)", re.M)
PROTO_FIELD = re.compile(
    r"^\s*(?:(repeated|optional|required)\s+)?([\w.<>, ]+?)\s+(\w+)\s*=\s*(\d+)", re.M)
PROTO_RPC = re.compile(r"^\s*rpc\s+(\w+)\s*\(([^)]*)\)\s*returns\s*\(([^)]*)\)", re.M)


def shape_proto(_doc, raw: bytes) -> dict[str, str]:
    text = raw.decode("utf-8", "replace")
    out = {}
    blocks = list(PROTO_BLOCK.finditer(text))
    for i, m in enumerate(blocks):
        end = blocks[i + 1].start() if i + 1 < len(blocks) else len(text)
        body = text[m.start():end]
        kind, name = m.group(1), m.group(2)
        if kind == "service":
            members = sorted(
                {"rpc": r[0], "in": r[1].strip(), "out": r[2].strip()}.items().__str__()
                for r in PROTO_RPC.findall(body)
            )
        else:
            members = sorted(
                "%s %s %s = %s" % (f[0] or "", f[1].strip(), f[2], f[3])
                for f in PROTO_FIELD.findall(body)
            )
        out["%s %s" % (kind, name)] = digest(members)
    return out


def shape_raw(_doc, raw: bytes) -> dict[str, str]:
    """No structure understood: one entry, so a change is still seen."""
    return {"file": hashlib.sha256(raw).hexdigest()[:16]}


SHAPES = {
    "openapi": (shape_openapi, True),
    "asyncapi": (shape_asyncapi, True),
    "sbe_xml": (shape_sbe_xml, False),
    "fix_xml": (shape_fix_xml, False),
    "postman": (shape_postman, True),
    "proto": (shape_proto, False),
    "raw": (shape_raw, False),
}


# --------------------------------------------------------------------------- #
# sources
# --------------------------------------------------------------------------- #

@dataclass
class Source:
    key: str
    venue: str
    fmt: str
    url: str
    note: str = ""
    connectors: tuple = ()

    @property
    def snapshot(self) -> Path:
        return SNAPSHOTS / ("%s.json" % self.key)


def load_sources() -> list[Source]:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    out = []
    for entry in data["sources"]:
        out.append(Source(
            key=entry["key"], venue=entry["venue"], fmt=entry["format"],
            url=entry["url"], note=entry.get("note", ""),
            connectors=tuple(entry.get("connectors", ())),
        ))
    unknown = sorted({s.fmt for s in out} - set(SHAPES))
    if unknown:
        raise SystemExit("specs.json names formats that do not exist: %s" % ", ".join(unknown))
    return out


def build_shape(source: Source, timeout: int) -> dict[str, str]:
    raw = fetch(source.url, timeout)
    extract, needs_parse = SHAPES[source.fmt]
    doc = parse_document(raw, source.url) if needs_parse else None
    shape = extract(doc, raw)
    if not shape:
        raise RuntimeError("nothing recognisable in the document (%d bytes)" % len(raw))
    return shape


def compare(old: dict[str, str], new: dict[str, str]):
    gone = sorted(set(old) - set(new))
    added = sorted(set(new) - set(old))
    changed = sorted(k for k in set(old) & set(new) if old[k] != new[k])
    return gone, added, changed


# --------------------------------------------------------------------------- #
# commands
# --------------------------------------------------------------------------- #

def cmd_list(sources: list[Source]) -> int:
    for s in sources:
        state = "recorded" if s.snapshot.exists() else "never recorded"
        print("  %-26s %-9s %-14s %s" % (s.key, s.fmt, state, ", ".join(s.connectors) or s.venue))
        print("      %s" % s.url)
        if s.note:
            print("      %s" % s.note)
    return 0


def cmd_check(sources: list[Source], timeout: int, update: bool) -> int:
    changed_any = False
    unreachable = []
    touched: dict[str, list[str]] = {}

    for s in sources:
        try:
            new = build_shape(s, timeout)
        except (urllib.error.URLError, urllib.error.HTTPError, RuntimeError, ValueError) as error:
            unreachable.append((s, error))
            print("  %-26s could not be read: %s" % (s.key, str(error)[:90]))
            continue

        if not s.snapshot.exists():
            print("  %-26s %d entries, first time seen" % (s.key, len(new)))
            if update:
                write_snapshot(s, new)
            changed_any = True
            continue

        old = json.loads(s.snapshot.read_text(encoding="utf-8"))["shape"]
        gone, added, altered = compare(old, new)

        if not (gone or added or altered):
            print("  %-26s %d entries, unchanged" % (s.key, len(new)))
            continue

        changed_any = True
        for connector in s.connectors:
            touched.setdefault(connector, []).append(s.key)
        print("  %-26s %d entries: %d added, %d removed, %d changed"
              % (s.key, len(new), len(added), len(gone), len(altered)))
        for label, items in (("+", added), ("-", gone), ("~", altered)):
            for item in items[:12]:
                print("      %s %s" % (label, item))
            if len(items) > 12:
                print("      %s ... and %d more" % (label, len(items) - 12))
        if update:
            write_snapshot(s, new)

    if touched:
        # The point of the run: which connectors somebody has to go and look at.
        print()
        print("  Connectors to look at:")
        for connector, keys in sorted(touched.items()):
            print("      %-22s %s" % (connector, ", ".join(sorted(keys))))

    if unreachable:
        return 2
    return 1 if changed_any and not update else 0


def write_snapshot(source: Source, shape: dict[str, str]) -> None:
    SNAPSHOTS.mkdir(exist_ok=True)
    source.snapshot.write_text(
        json.dumps(
            {"key": source.key, "venue": source.venue, "format": source.fmt,
             "url": source.url, "entries": len(shape), "shape": dict(sorted(shape.items()))},
            indent=1, ensure_ascii=False, sort_keys=False,
        ) + "\n",
        encoding="utf-8",
    )


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass

    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("action", choices=["check", "update", "list"])
    parser.add_argument("--only", nargs="*", help="limit to these manifest keys or venues")
    parser.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT)
    args = parser.parse_args()

    sources = load_sources()
    if args.only:
        wanted = {w.lower() for w in args.only}
        sources = [s for s in sources if s.key.lower() in wanted or s.venue.lower() in wanted]
        if not sources:
            raise SystemExit("nothing in specs.json matches %s" % ", ".join(args.only))

    if args.action == "list":
        return cmd_list(sources)
    return cmd_check(sources, args.timeout, update=args.action == "update")


if __name__ == "__main__":
    sys.exit(main())
