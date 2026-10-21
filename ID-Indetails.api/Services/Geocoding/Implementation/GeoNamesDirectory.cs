using System.Globalization;
using System.IO.Compression;
using System.Text;
using ID_Indetails.api.Services.Geocoding.Abstraction;

namespace ID_Indetails.api.Services.Geocoding.Implementation;

/// <summary>
/// GeoNames' places of 1,000 people or more, and every village of the countries added from their own file
/// (https://www.geonames.org, CC BY 4.0), trimmed to one line per place: "name, country code, latitude, longitude,
/// population" and optionally the place's other names joined by "|", separated by tabs, gzipped, and built into the
/// assembly as <c>geonames-cities.tsv.gz</c> (Assets/build-geonames-cities.py). Read once, on first use. A place is found
/// by any of its names, and always shown by its main one. Without the file the lists are empty, and the API still runs.
/// </summary>
public sealed class GeoNamesDirectory : IPlaceDirectory
{
    public const string ResourceName = "geonames-cities.tsv.gz";

    private readonly Lazy<Data> _data;

    /// <param name="open">The gzipped list; the one built into the assembly when left out.</param>
    public GeoNamesDirectory(Func<Stream?>? open = null)
        => _data = new Lazy<Data>(() => Load(open ?? (() => typeof(GeoNamesDirectory).Assembly.GetManifestResourceStream(ResourceName))));

    public IReadOnlyList<DirectoryCountry> Countries => _data.Value.Countries;

    public DirectoryCountry? Country(string nameOrCode)
        => _data.Value.CountryByKey.GetValueOrDefault(Key(nameOrCode));

    public IReadOnlyList<DirectoryCity> Cities(string countryCode, string? search, int take)
    {
        var cities = _data.Value.CitiesOf(countryCode);
        if (string.IsNullOrWhiteSpace(search))
        {
            return cities.Take(take).Select(c => c.City).ToList();
        }

        var term = Key(search);
        bool Starts(Entry c) => c.Keys.Any(k => k.StartsWith(term, StringComparison.Ordinal));
        return cities.Where(Starts)
            .Concat(cities.Where(c => !Starts(c) && c.Keys.Any(k => k.Contains(term, StringComparison.Ordinal))))
            .Take(take)
            .Select(c => c.City)
            .ToList();
    }

    public IReadOnlyList<DirectoryCity> Elsewhere(string search, string exceptCountryCode, int take)
    {
        var term = Key(search);
        if (term.Length < 3)
        {
            return [];
        }

        var except = exceptCountryCode.ToUpperInvariant();
        return _data.Value.All
            .Where(e => e.City.CountryCode != except && e.Keys.Any(k => k.StartsWith(term, StringComparison.Ordinal)))
            .OrderByDescending(e => e.Keys.Contains(term))
            .ThenByDescending(e => e.City.Population)
            .Take(take)
            .Select(e => e.City)
            .ToList();
    }

    public DirectoryCity? City(string countryCode, string name)
    {
        var key = Key(name);
        // its main name first, then any other; biggest first, so the first match is the one most people mean
        var cities = _data.Value.CitiesOf(countryCode);
        return (cities.FirstOrDefault(c => c.Keys[0] == key) ?? cities.FirstOrDefault(c => c.Keys.Contains(key)))?.City;
    }

    public DirectoryCity? Nearest(decimal latitude, decimal longitude, double maxKilometres)
    {
        DirectoryCity? nearest = null;
        var best = maxKilometres;
        double lat = (double)latitude, lng = (double)longitude;
        foreach (var entry in _data.Value.All)
        {
            var city = entry.City;
            // a cheap box first: a degree of latitude is about 111 km
            if (Math.Abs((double)city.Latitude - lat) * 111 > best)
            {
                continue;
            }

            var distance = Kilometres(lat, lng, (double)city.Latitude, (double)city.Longitude);
            if (distance <= best)
            {
                best = distance;
                nearest = city;
            }
        }

        return nearest;
    }

    public DirectoryCity? RandomCity(string countryCode, Random random)
    {
        var cities = _data.Value.CitiesOf(countryCode);
        return cities.Count == 0 ? null : cities[random.Next(cities.Count)].City;
    }

    /// <summary>A name as it is compared: accents dropped, lowercase, single spaces.</summary>
    public static string Key(string value)
    {
        var plain = new StringBuilder(value.Length);
        foreach (var c in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(c));
            }
        }

        return string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double Kilometres(double lat1, double lng1, double lat2, double lng2)
    {
        const double radius = 6371;
        double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * radius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static Data Load(Func<Stream?> open)
    {
        using var raw = open();
        if (raw is null)
        {
            return new Data([]);
        }

        using var reader = new StreamReader(new GZipStream(raw, CompressionMode.Decompress), Encoding.UTF8);
        var cities = new List<Entry>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length < 5
                || !decimal.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !decimal.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);
            var city = new DirectoryCity(parts[0], parts[1].ToUpperInvariant(), latitude, longitude, population);
            var others = parts.Length > 5 ? parts[5].Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Key) : [];
            cities.Add(new Entry(city, [Key(city.Name), .. others.Distinct()]));
        }

        return new Data(cities);
    }

    /// <summary>A place and its names as compared: the main one first, then the others.</summary>
    private sealed record Entry(DirectoryCity City, string[] Keys);

    private sealed class Data
    {
        private readonly Dictionary<string, List<Entry>> _byCountry;

        public Data(List<Entry> all)
        {
            All = all;
            _byCountry = all.GroupBy(e => e.City.CountryCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.City.Population).ThenBy(e => e.City.Name, StringComparer.Ordinal).ToList());
            Countries = _byCountry.Keys.Select(code => new DirectoryCountry(code, NameOf(code))).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            CountryByKey = new Dictionary<string, DirectoryCountry>();
            foreach (var country in Countries)
            {
                CountryByKey.TryAdd(Key(country.Code), country);
                CountryByKey.TryAdd(Key(country.Name), country);
            }
        }

        public List<Entry> All { get; }

        public IReadOnlyList<DirectoryCountry> Countries { get; }

        public Dictionary<string, DirectoryCountry> CountryByKey { get; }

        public List<Entry> CitiesOf(string countryCode) => _byCountry.GetValueOrDefault(countryCode.ToUpperInvariant()) ?? [];

        /// <summary>.NET knows nearly every country's English name; a code it doesn't know (such as Kosovo's XK) shows as itself.</summary>
        private static string NameOf(string code)
        {
            try
            {
                return new RegionInfo(code).EnglishName;
            }
            catch (ArgumentException)
            {
                return code;
            }
        }
    }
}
