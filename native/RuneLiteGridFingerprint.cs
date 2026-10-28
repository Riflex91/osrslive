using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OSRSLiveDiagnostics;

// In-memory, whole-grid pixel identity for an ALREADY independently confirmed
// client-API tile. Handles missing reference glyphs without ever naming or
// guessing an OCR digit: a current screen must have EXACTLY the same visible
// 8 X/Y glyphs, plane glyph and 11 Chunk/Region glyphs as two later trusted
// frames. Fingerprints are never written, logged or sent anywhere.
internal sealed class RuneLiteGridFingerprint
{
    private const int Pitch=7,Height=11,RowPitch=15,WhiteThreshold=195;
    private static readonly int[] TileDigits=[4,5,6,7,10,11,12,13,16];
    private WorldTile? pendingTile,trustedTile;
    private DateTimeOffset pendingCaptured,trustedCaptured;
    private HashSet<string> pendingHashes=new(StringComparer.Ordinal);
    private HashSet<string> trustedHashes=new(StringComparer.Ordinal);

    internal void Clear()
    {
        pendingTile=null;trustedTile=null;pendingCaptured=default;trustedCaptured=default;
        pendingHashes.Clear();trustedHashes.Clear();
    }

    internal string ObserveConfirmedFresh(WorldTile tile,Bitmap bitmap,DateTimeOffset captured)
    {
        if(captured==default)return "stage=GRID_FINGERPRINT result=NO_CAPTURE_TIME";
        var current=ReadSignatures(bitmap);
        if(trustedTile!=tile)
        {
            trustedTile=null;trustedHashes.Clear();trustedCaptured=default;
        }
        bool independent=pendingTile==tile&&captured>pendingCaptured&&
            captured-pendingCaptured>=TimeSpan.FromMilliseconds(300)&&
            captured-pendingCaptured<=TimeSpan.FromSeconds(3);
        var common=independent?new HashSet<string>(pendingHashes,StringComparer.Ordinal):
            new HashSet<string>(StringComparer.Ordinal);
        common.IntersectWith(current);
        if(common.Count>0)
        {
            trustedTile=tile;trustedHashes=common;trustedCaptured=captured;
        }
        pendingTile=tile;pendingCaptured=captured;pendingHashes=current;
        return $"stage=GRID_FINGERPRINT result={(common.Count>0?"TRUSTED_AFTER_TWO_CONFIRMED_FRAMES":"AWAITING_SECOND_MATCH")} "+
            $"current_candidates={current.Count} shared_candidates={common.Count} "+
            "source=POST_EXPORT_PIXEL_GLYPHS no_raw_pixels_persisted=true";
    }

    internal bool MatchesStaleExport(WorldTile tile,Bitmap bitmap,DateTimeOffset captured,
        out string diagnostic)
    {
        if(trustedTile!=tile||trustedCaptured==default||captured<=trustedCaptured||
           captured-trustedCaptured>TimeSpan.FromMinutes(5)||trustedHashes.Count==0)
        {
            diagnostic="stage=GRID_FINGERPRINT result=NO_TRUSTED_REFERENCE "+
                "current_position_not_assumed=true";
            return false;
        }
        var actual=ReadSignatures(bitmap);
        actual.IntersectWith(trustedHashes);
        bool verified=actual.Count>0;
        if(verified)trustedCaptured=captured; // extend only after new real screen proof
        diagnostic=$"stage=GRID_FINGERPRINT result={(verified?"EXACT_CURRENT_PANEL_MATCH":"CURRENT_PANEL_DIFFERENT")} "+
            $"matching_signatures={actual.Count} trusted_signatures={trustedHashes.Count} "+
            "tile_bound_to_prior_direct_api=true unique_screen_glyphs=true "+
            "no_raw_pixels_persisted=true no_position_guessing=true";
        return verified;
    }

    private static HashSet<string> ReadSignatures(Bitmap bitmap)
    {
        var found=new HashSet<string>(StringComparer.Ordinal);
        int width=Math.Min(bitmap.Width,350),height=Math.Min(bitmap.Height,220);
        if(width<119||height<41)return found;
        var white=new bool[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var c=bitmap.GetPixel(x,y);
            white[y*width+x]=c.R>=WhiteThreshold&&c.G>=WhiteThreshold&&c.B>=WhiteThreshold;
        }
        RuneLitePixelGridReader.Glyph Cell(int ox,int oy,int row,int col)
        {
            ulong low=0,high=0;
            int x0=ox+col*Pitch,y0=oy+row*RowPitch;
            for(int y=0;y<Height;y++)for(int x=0;x<Pitch;x++)
            {
                if(!white[(y0+y)*width+x0+x])continue;
                int bit=y*Pitch+x;
                if(bit<64)low|=1UL<<bit;else high|=1UL<<(bit-64);
            }
            return new(low,high);
        }
        for(int oy=0;oy<=height-(2*RowPitch+Height);oy++)
        for(int ox=0;ox<=width-17*Pitch;ox++)
        {
            var comma=Cell(ox,oy,0,8);
            if(comma.Ink is <3 or >9||comma.Distance(Cell(ox,oy,0,14))>2||
               Cell(ox,oy,0,9).Ink>1||Cell(ox,oy,0,15).Ink>1||
               (comma.Low&((1UL<<56)-1))!=0||comma.High==0)continue;
            // Only the actual numeric cells are fingerprinted. The game
            // background and tooltip content are never retained or hashed.
            var cells=new List<RuneLitePixelGridReader.Glyph>(20);
            foreach(int col in TileDigits)cells.Add(Cell(ox,oy,0,col));
            for(int col=11;col<=16;col++)cells.Add(Cell(ox,oy,1,col));
            for(int col=12;col<=16;col++)cells.Add(Cell(ox,oy,2,col));
            if(cells.Any(c=>c.Ink is <5 or >44))continue;
            Span<byte> bytes=stackalloc byte[20*16];
            for(int i=0;i<cells.Count;i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i*16,8),cells[i].Low);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(i*16+8,8),cells[i].High);
            }
            found.Add(Convert.ToHexString(SHA256.HashData(bytes)));
            if(found.Count>=16)return found;
        }
        return found;
    }
}
