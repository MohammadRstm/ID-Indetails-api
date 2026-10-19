using System.Globalization;
using System.IO.Compression;
using System.Text;
using ID_Indetails.api.Services.Geocoding.Implementation;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>A few towns, in the bundled list's format, for the place lists in tests (the real list is large).</summary>
public static class TestPlaces
{
    public sealed record Town(string Name, string CountryCode, decimal Latitude, decimal Longitude, int Population);

    public static readonly Town Beirut = new("Beirut", "LB", 33.888940m, 35.494420m, 1916100);
    public static readonly Town Tripoli = new("Tripoli", "LB", 34.436670m, 35.849720m, 229398);
    public static readonly Town Jbeil = new("Jbeil", "LB", 34.121110m, 35.648060m, 40000);
    public static readonly Town Batroun = new("Batroun", "LB", 34.255280m, 35.658060m, 9000);
    public static readonly Town Paris = new("Paris", "FR", 48.853410m, 2.348800m, 2138551);

    public static readonly IReadOnlyList<Town> Lebanese = [Beirut, Tripoli, Jbeil, Batroun];

    public static GeoNamesDirectory Directory() => new(() =>
    {
        var lines = string.Join('\n', Lebanese.Append(Paris).Select(t => string.Join('\t',
            t.Name, t.CountryCode, t.Latitude.ToString(CultureInfo.InvariantCulture), t.Longitude.ToString(CultureInfo.InvariantCulture),
            t.Population.ToString(CultureInfo.InvariantCulture))));
        var zipped = new MemoryStream();
        using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(lines));
        }

        zipped.Position = 0;
        return zipped;
    });
}
