using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// An invisible mark in a picture's pixels that says it is the company's, and which picture it is. The picture is seen
/// as a 256×256 grid of 16×16 blocks, whatever its size; each block carries one bit of a 32-bit code (a tag and the
/// picture's id) in the balance of two mid-frequency values, nudged by a secret key, each bit in 8 blocks. The nudge is
/// capped so that, scaled back up, it changes no pixel by more than about 3 levels in 255, smoothly: it isn't seen. A
/// block whose own detail is too strong to nudge that far is simply outvoted by the others; and because the mark is
/// read on the same grid it survives resizing, re-saving as JPEG or WebP, and screenshots at any size. Heavy cropping,
/// filters or a tool made to remove it can defeat it.
/// </summary>
public static class Watermark
{
    private const int Grid = 256;
    private const int Block = 16;
    private const int BlocksPerSide = Grid / Block;
    private const int BlockCount = BlocksPerSide * BlocksPerSide;
    private const int Bits = 32;

    /// <summary>The code's first byte: the company's, so a picture that isn't marked reads as no mark.</summary>
    private const uint Tag = 0xA7;

    /// <summary>How far apart the two values are pushed: big enough to survive re-saving, small enough to stay unseen.</summary>
    private const float Margin = 24f;

    /// <summary>The cosine basis of a 16-point DCT (orthonormal).</summary>
    private static readonly float[,] Basis = MakeBasis();

    public sealed record Reading(bool Found, long PictureId, double Confidence);

    /// <summary>Marks the picture in place with <paramref name="pictureId"/> (its lowest 24 bits).</summary>
    public static void Embed(SKBitmap picture, long pictureId, string key)
    {
        var plan = Plan(key);
        var code = (Tag << 24) | ((uint)pictureId & 0xFFFFFF);
        var luma = Luma(picture);
        var nudge = new float[Grid, Grid];

        for (var b = 0; b < BlockCount; b++)
        {
            var (by, bx) = (b / BlocksPerSide * Block, b % BlocksPerSide * Block);
            var wanted = ((code >> plan.Bit[b]) & 1) == 1 ? plan.Sign[b] : -plan.Sign[b];
            var difference = Coefficient(luma, by, bx, 2, 3) - Coefficient(luma, by, bx, 3, 2);
            var short_ = Margin - wanted * difference;
            if (short_ <= 0)
            {
                continue;
            }

            // half the shortfall on each value, the opposite ways; never more than the margin, so a busy block (an edge
            // against the bit) stays unseen and is outvoted by the other blocks carrying that bit instead
            var change = wanted * Math.Min(short_, Margin) / 2f;
            for (var y = 0; y < Block; y++)
            {
                for (var x = 0; x < Block; x++)
                {
                    nudge[by + y, bx + x] += change * (Basis[2, y] * Basis[3, x] - Basis[3, y] * Basis[2, x]);
                }
            }
        }

        Apply(picture, nudge);
    }

    /// <summary>
    /// Whether the picture carries the mark, which picture it says, and the share of blocks that agree (about 0.5 by
    /// chance; 0.9 or more for a marked picture, a little less after heavy resizing).
    /// </summary>
    public static Reading Read(SKBitmap picture, string key)
    {
        var plan = Plan(key);
        var luma = Luma(picture);
        var differences = new float[BlockCount];
        var votes = new int[Bits];
        for (var b = 0; b < BlockCount; b++)
        {
            var (by, bx) = (b / BlocksPerSide * Block, b % BlocksPerSide * Block);
            differences[b] = Coefficient(luma, by, bx, 2, 3) - Coefficient(luma, by, bx, 3, 2);
            // one vote a block, whatever its strength: a busy block can't outshout the others
            votes[plan.Bit[b]] += plan.Sign[b] * Math.Sign(differences[b]);
        }

        uint code = 0;
        for (var bit = 0; bit < Bits; bit++)
        {
            if (votes[bit] > 0)
            {
                code |= 1u << bit;
            }
        }

        // the share of blocks that agree with what was read: about a half by chance, near all for a marked picture
        var agreeing = Enumerable.Range(0, BlockCount)
            .Count(b => plan.Sign[b] * Math.Sign(differences[b]) == (((code >> plan.Bit[b]) & 1) == 1 ? 1 : -1));
        var confidence = (double)agreeing / BlockCount;
        return code >> 24 == Tag && confidence >= 0.7
            ? new Reading(true, code & 0xFFFFFF, Math.Round(confidence, 2))
            : new Reading(false, 0, Math.Round(confidence, 2));
    }

    /// <summary>Which bit each block carries and which way, from the key: without it the blocks can't be read.</summary>
    private static (int[] Bit, int[] Sign) Plan(string key)
    {
        var bits = new int[BlockCount];
        var signs = new int[BlockCount];
        var order = Enumerable.Range(0, BlockCount).Select(b => b % Bits).ToArray();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var stream = new List<byte>();
        for (var round = 0; stream.Count < BlockCount * 5; round++)
        {
            stream.AddRange(hmac.ComputeHash(BitConverter.GetBytes(round)));
        }

        // a keyed shuffle (Fisher–Yates) of which bit goes where, and a keyed sign for each block
        for (var i = BlockCount - 1; i > 0; i--)
        {
            var j = (int)(BitConverter.ToUInt32(stream.ToArray(), i * 4) % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (var b = 0; b < BlockCount; b++)
        {
            bits[b] = order[b];
            signs[b] = (stream[BlockCount * 4 + b] & 1) == 0 ? 1 : -1;
        }

        return (bits, signs);
    }

    /// <summary>The picture's brightness on the 256×256 grid.</summary>
    private static float[,] Luma(SKBitmap picture)
    {
        using var small = picture.Resize(new SKSizeI(Grid, Grid), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var pixels = small.Pixels;
        var luma = new float[Grid, Grid];
        for (var y = 0; y < Grid; y++)
        {
            for (var x = 0; x < Grid; x++)
            {
                var c = pixels[y * Grid + x];
                luma[y, x] = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;
            }
        }

        return luma;
    }

    private static float Coefficient(float[,] luma, int by, int bx, int u, int v)
    {
        float sum = 0;
        for (var y = 0; y < Block; y++)
        {
            for (var x = 0; x < Block; x++)
            {
                sum += Basis[u, y] * Basis[v, x] * luma[by + y, bx + x];
            }
        }

        return sum;
    }

    /// <summary>Adds the grid's nudge to every pixel, scaled up smoothly to the picture's size.</summary>
    private static void Apply(SKBitmap picture, float[,] nudge)
    {
        var pixels = picture.Pixels;
        for (var y = 0; y < picture.Height; y++)
        {
            var gy = Math.Clamp((y + 0.5f) * Grid / picture.Height - 0.5f, 0, Grid - 1);
            var y0 = (int)gy;
            var y1 = Math.Min(y0 + 1, Grid - 1);
            var fy = gy - y0;
            for (var x = 0; x < picture.Width; x++)
            {
                var gx = Math.Clamp((x + 0.5f) * Grid / picture.Width - 0.5f, 0, Grid - 1);
                var x0 = (int)gx;
                var x1 = Math.Min(x0 + 1, Grid - 1);
                var fx = gx - x0;
                var n = (nudge[y0, x0] * (1 - fx) + nudge[y0, x1] * fx) * (1 - fy) + (nudge[y1, x0] * (1 - fx) + nudge[y1, x1] * fx) * fy;
                var c = pixels[y * picture.Width + x];
                pixels[y * picture.Width + x] = new SKColor(Shift(c.Red, n), Shift(c.Green, n), Shift(c.Blue, n), c.Alpha);
            }
        }

        picture.Pixels = pixels;
    }

    private static byte Shift(byte channel, float by) => (byte)Math.Clamp((int)Math.Round(channel + by), 0, 255);

    private static float[,] MakeBasis()
    {
        var basis = new float[Block, Block];
        for (var k = 0; k < Block; k++)
        {
            var scale = k == 0 ? Math.Sqrt(1.0 / Block) : Math.Sqrt(2.0 / Block);
            for (var n = 0; n < Block; n++)
            {
                basis[k, n] = (float)(scale * Math.Cos(Math.PI * (2 * n + 1) * k / (2.0 * Block)));
            }
        }

        return basis;
    }
}
