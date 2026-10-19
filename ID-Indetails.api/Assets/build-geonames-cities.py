"""
Builds geonames-cities.tsv.gz, the website's country and city lists (Services/Geocoding/Implementation/GeoNamesDirectory.cs),
from GeoNames' list of places of 1,000 people or more (https://download.geonames.org/export/dump/cities1000.zip,
CC BY 4.0, https://www.geonames.org). Keeps one line per place: name, country code, latitude, longitude, population.

    python -I build-geonames-cities.py path/to/cities1000.zip

Writes geonames-cities.tsv.gz next to this script. Run it again to refresh the list (a year apart is plenty).
"""
import gzip
import io
import os
import sys
import zipfile

source = sys.argv[1]
target = os.path.join(os.path.dirname(os.path.abspath(__file__)), "geonames-cities.tsv.gz")

rows = []
with zipfile.ZipFile(source) as archive:
    with archive.open("cities1000.txt") as raw:
        for line in io.TextIOWrapper(raw, encoding="utf-8"):
            # geonameid, name, asciiname, alternatenames, latitude, longitude, feature class, feature code, country code, ...
            # population is the 15th column
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 15 or not parts[8]:
                continue
            name, country = parts[1].strip(), parts[8].strip().upper()
            latitude, longitude = round(float(parts[4]), 5), round(float(parts[5]), 5)
            population = int(parts[14] or 0)
            rows.append((country, -population, name, latitude, longitude, population))

rows.sort()
with gzip.open(target, "wt", encoding="utf-8", compresslevel=9, newline="\n") as out:
    for country, _, name, latitude, longitude, population in rows:
        out.write(f"{name}\t{country}\t{latitude}\t{longitude}\t{population}\n")

print(f"{target}: {len(rows)} places in {len({r[0] for r in rows})} countries, {os.path.getsize(target) / 1e6:.1f} MB")
