"""
Builds geonames-cities.tsv.gz, the website's country and city lists (Services/Geocoding/Implementation/GeoNamesDirectory.cs),
from GeoNames' list of places of 1,000 people or more (https://download.geonames.org/export/dump/cities1000.zip,
CC BY 4.0, https://www.geonames.org). Keeps one line per place: name, country code, latitude, longitude, population, and
for a country added from its own file (below) the other names the place is known by, separated by "|", so typing
"Gemmayze" finds "Ej Jimmaïze".

A country's own file (e.g. https://download.geonames.org/export/dump/LB.zip) can be added after it: that country's places
then come from it instead, every populated place (villages, neighbourhoods) rather than only those of 1,000 people.

    python -I build-geonames-cities.py path/to/cities1000.zip [path/to/LB.zip ...]

Writes geonames-cities.tsv.gz next to this script. Run it again to refresh the list (a year apart is plenty).
"""
import gzip
import io
import os
import re
import sys
import zipfile

# Populated places worth choosing: cities, towns, villages, localities and neighbourhoods. Left out: abandoned (PPLQ),
# historical (PPLH, PPLCH) and destroyed (PPLW) places.
KEPT = {"PPL", "PPLA", "PPLA2", "PPLA3", "PPLA4", "PPLA5", "PPLC", "PPLG", "PPLL", "PPLR", "PPLS", "PPLX", "PPLF"}

# Another name worth searching: Latin letters (accents allowed), spaces, dashes and apostrophes, starting with a capital.
# GeoNames also lists codes, links and letter-for-letter transcriptions such as "dhwqmsbh", which this leaves out.
ALTERNATE = re.compile(r"^[A-ZÀ-Þ][A-Za-zÀ-ɏ' .-]{2,39}$")


def alternates(name, raw):
    """The place's other names worth searching, each once, at most 24."""
    seen, kept = {name.casefold()}, []
    for alt in raw.split(","):
        alt = alt.strip()
        if ALTERNATE.match(alt) and alt.casefold() not in seen:
            seen.add(alt.casefold())
            kept.append(alt)
    return kept[:24]


def places(path, member, own_file):
    """Rows of a GeoNames dump: (country, name, latitude, longitude, population, other names)."""
    with zipfile.ZipFile(path) as archive:
        with archive.open(member) as raw:
            for line in io.TextIOWrapper(raw, encoding="utf-8"):
                # geonameid, name, asciiname, alternatenames, latitude, longitude, feature class, feature code,
                # country code, ...; population is the 15th column
                parts = line.rstrip("\n").split("\t")
                if len(parts) < 15 or not parts[8]:
                    continue
                if own_file and (parts[6] != "P" or parts[7] not in KEPT):
                    continue
                name = parts[1].strip()
                others = alternates(name, parts[3]) if own_file else []
                yield parts[8].strip().upper(), name, round(float(parts[4]), 5), round(float(parts[5]), 5), int(parts[14] or 0), others


source, extras = sys.argv[1], sys.argv[2:]
target = os.path.join(os.path.dirname(os.path.abspath(__file__)), "geonames-cities.tsv.gz")

rows = list(places(source, "cities1000.txt", own_file=False))
for extra in extras:
    country = os.path.splitext(os.path.basename(extra))[0].upper()
    own = [r for r in places(extra, f"{country}.txt", own_file=True) if r[0] == country]
    rows = [r for r in rows if r[0] != country] + own
    print(f"{country}: {len(own)} places from {os.path.basename(extra)}")

rows.sort(key=lambda r: (r[0], -r[4], r[1]))
with gzip.open(target, "wt", encoding="utf-8", compresslevel=9, newline="\n") as out:
    for country, name, latitude, longitude, population, others in rows:
        tail = "\t" + "|".join(others) if others else ""
        out.write(f"{name}\t{country}\t{latitude}\t{longitude}\t{population}{tail}\n")

print(f"{target}: {len(rows)} places in {len({r[0] for r in rows})} countries, {os.path.getsize(target) / 1e6:.1f} MB")
