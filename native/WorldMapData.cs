using System.IO.Compression;
using System.Security.Cryptography;
using Cv=OpenCvSharp;

namespace OSRSLiveDiagnostics;

internal sealed record MapTransport(WorldTile From,WorldTile To,string Action,string Object);
internal sealed record PathStep(WorldTile Tile,MapTransport? Transport=null);

internal sealed class WorldMapData:IDisposable
{
    // Public surface-map regions, including a border for localisation. Routing
    // stays in the F2P mainland and below the Wilderness boundary.
    internal const int MinX=2880,MaxX=3456,MinY=3072,MaxY=3584;
    internal const string MapCommit="1937ccfa4007345ef62f7694e773509deac8a1d7";
    internal const string CollisionCommit="537092df2be681b7f47b488c1adc7be40c8d7b59";
    internal const string CollisionHash="28A3B72D8E7167876C45B9EB557C5EFC56896641D0C2EAC66835E3442EC653DE";
    internal readonly Dictionary<(int X,int Y),byte[]> Regions=new();
    internal readonly List<MapTransport> Transports=new();
    internal readonly List<ReferenceMap> References=new();
    // World-map symbols and native minimap sprites have different placement.
    // Keep these independent: the large-map localizer still uses References.
    internal readonly List<ReferenceMap> MinimapReferences=new();
    internal sealed class ReferenceMap:IDisposable
    {
        internal readonly int Plane,OriginX,OriginY;internal readonly Cv.Mat Image,Descriptors=new();internal readonly Cv.KeyPoint[] Points;
        internal ReferenceMap(int plane,Cv.Mat image,int originX=0,int originY=0)
        {
            Plane=plane;Image=image;OriginX=originX;OriginY=originY;using var sift=Cv.Features2D.SIFT.Create(18000,contrastThreshold:.015);
            sift.DetectAndCompute(image,null,out Points,Descriptors);
        }
        public void Dispose(){Image.Dispose();Descriptors.Dispose();}
    }
    internal static async Task<WorldMapData> Load(Action<string> report,CancellationToken ct)
    {
        var data=new WorldMapData();
        try
        {
            data.LoadMinimapReferences();
            report("NATIVE_MINIMAP_REFERENCES_READY planes=4 pixels_per_tile=4");
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=4*1024*1024};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OSRS-Play/1.7.0 (+https://github.com/Riflex91/osrslive)");
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","maps");
            async Task<byte[]> Get(string url,string relative,string? sha=null,bool optional=false)
            {
                string path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
                if(File.Exists(path))
                {
                    var stored=await File.ReadAllBytesAsync(path,ct);
                    if(stored.Length<4*1024*1024&&(sha is null||Convert.ToHexString(SHA256.HashData(stored))==sha))return stored;
                }
                using var response=await client.GetAsync(url,ct);
                if(optional&&response.StatusCode==System.Net.HttpStatusCode.NotFound)return Array.Empty<byte>();
                response.EnsureSuccessStatusCode();var bytes=await response.Content.ReadAsByteArrayAsync(ct);
                if(sha is not null&&Convert.ToHexString(SHA256.HashData(bytes))!=sha)throw new RoutineBlockedException("NAVIGATION_DATA_HASH_MISMATCH");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temp,bytes,ct);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
                return bytes;
            }
            report("LOADING_PUBLIC_COLLISION_MAP");
            var collision=await Get($"https://raw.githubusercontent.com/Skretzo/shortest-path/{CollisionCommit}/src/main/resources/collision-map.zip",CollisionCommit+"/collision-map.zip",CollisionHash);
            data.ReadCollision(collision);
            var transportBytes=await Get($"https://raw.githubusercontent.com/Skretzo/shortest-path/{CollisionCommit}/src/main/resources/transports/transports.tsv",CollisionCommit+"/transports.tsv");
            data.ReadTransports(System.Text.Encoding.UTF8.GetString(transportBytes));
            using var concurrency=new SemaphoreSlim(8);
            for(int plane=0;plane<4;plane++)
            {
                report("LOADING_REFERENCE_MAP_PLANE_"+plane);
                var tiles=new System.Collections.Concurrent.ConcurrentBag<(int X,int Y,byte[] Bytes)>();
                var work=Enumerable.Range(MinX/64,(MaxX-MinX)/64).SelectMany(x=>
                    Enumerable.Range(MinY/64,(MaxY-MinY)/64).Select(y=>(X:x,Y:y))).Select(async region=>
                {
                    await concurrency.WaitAsync(ct);
                    try
                    {
                        // Explv's zoom-8 tiles use four pixels per game tile;
                        // source coordinate conversion is documented in its
                        // Position.js. TMS y is measured from the south.
                        int tx=region.X-15,ty=region.Y-19;
                        string relative=$"{MapCommit}/{plane}/8/{tx}/{ty}.png";
                        var bytes=await Get("https://raw.githubusercontent.com/Explv/osrs_map_tiles/"+relative,relative,optional:plane>0);
                        tiles.Add((region.X,region.Y,bytes));
                    }
                    finally{concurrency.Release();}
                }).ToArray();
                await Task.WhenAll(work);
                var image=new Cv.Mat((MaxY-MinY)*4,(MaxX-MinX)*4,Cv.MatType.CV_8UC1,Cv.Scalar.Black);
                foreach(var tile in tiles)
                {
                    if(tile.Bytes.Length==0)continue;
                    using var decoded=Cv.Cv2.ImDecode(tile.Bytes,Cv.ImreadModes.Grayscale);
                    if(decoded.Width!=256||decoded.Height!=256)throw new RoutineBlockedException("REFERENCE_MAP_TILE_INVALID");
                    using var roi=new Cv.Mat(image,new Cv.Rect((tile.X*64-MinX)*4,(MaxY-(tile.Y+1)*64)*4,256,256));decoded.CopyTo(roi);
                }
                try{var reference=await Task.Run(()=>new ReferenceMap(plane,image),ct);data.References.Add(reference);}
                catch{image.Dispose();throw;}
            }
            report("AUTOMATIC_WORLD_NAVIGATION_READY");return data;
        }
        catch{data.Dispose();throw;}
    }
    internal void ReadCollision(byte[] bytes)
    {
        using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
        foreach(var entry in zip.Entries)
        {
            var parts=entry.Name.Split('_');
            if(parts.Length!=2||!int.TryParse(parts[0],out int x)||!int.TryParse(parts[1],out int y))continue;
            if(x<MinX/64||x>=MaxX/64||y<MinY/64||y>=MaxY/64)continue;
            if(entry.Length>8192)throw new RoutineBlockedException("COLLISION_REGION_FORMAT_CHANGED");
            using var stream=entry.Open();using var buffer=new MemoryStream();stream.CopyTo(buffer);Regions[(x,y)]=buffer.ToArray();
        }
        if(Regions.Count<30)throw new RoutineBlockedException("COLLISION_MAP_INCOMPLETE");
    }
    internal static bool Domain(WorldTile t)=>t.X is >=2944 and <3392&&t.Y is >=3072 and <3520&&t.Plane is >=0 and <=3;
    internal bool Flag(WorldTile t,int flag)
    {
        if(t.Plane is <0 or >3||!Regions.TryGetValue((t.X/64,t.Y/64),out var bytes))return false;
        int bit=((t.Plane*4096+(t.Y&63)*64+(t.X&63))*4)+flag;
        return bit/8<bytes.Length&&(bytes[bit/8]&(1<<(bit%8)))!=0;
    }
    internal bool Move(WorldTile from,WorldTile to)
    {
        if(!Domain(from)||!Domain(to)||from.Plane!=to.Plane||from.Distance(to)!=1)return false;
        int dx=to.X-from.X,dy=to.Y-from.Y;
        if(dx==0)return dy==1?Flag(from,0):Flag(to,0);
        if(dy==0)return dx==1?Flag(from,1):Flag(to,1);
        // All four cardinal edges must be open. Never cut a wall's corner.
        var x=new WorldTile(to.X,from.Y,from.Plane);var y=new WorldTile(from.X,to.Y,from.Plane);
        return Move(from,x)&&Move(from,y)&&Move(x,to)&&Move(y,to);
    }
    internal bool Boundary(WorldTile from,WorldTile to)=>from.X==to.X?
        Flag(from.Y<to.Y?from:to,2):from.Y==to.Y&&Flag(from.X<to.X?from:to,3);
    private void ReadTransports(string text)
    {
        foreach(var line in text.Split('\n'))
        {
            if(line.StartsWith('#'))continue;var fields=line.TrimEnd('\r').Split('\t');
            // Only unconditional stairs/doors from public data; no inferred
            // quest unlocks, fees, teleports, membership or item requirements.
            if(fields.Length<8||fields.Skip(3).Take(5).Any(s=>s.Length>0))continue;
            WorldTile? Tile(string s){var n=s.Split(' ');return n.Length==3&&int.TryParse(n[0],out int x)&&int.TryParse(n[1],out int y)&&int.TryParse(n[2],out int z)?new(x,y,z):null;}
            var from=Tile(fields[0]);var to=Tile(fields[1]);
            var m=System.Text.RegularExpressions.Regex.Match(fields[2],@"^(Climb-up|Climb-down|Open) (Staircase|Stairs|Ladder|Door|Gate) \d+$");
            if(from is null||to is null||!Domain(from)||!Domain(to)||!m.Success)continue;
            Transports.Add(new(from,to,m.Groups[1].Value,m.Groups[2].Value));
        }
    }
    internal PathStep[]? FindPath(WorldTile start,WorldTile goal,int radius=2,CancellationToken ct=default)
    {
        if(!Domain(start)||!Domain(goal))return null;
        var queue=new PriorityQueue<WorldTile,int>();var cost=new Dictionary<WorldTile,int>{{start,0}};
        var parents=new Dictionary<WorldTile,(WorldTile From,MapTransport? Transport)>();queue.Enqueue(start,0);
        int Heuristic(WorldTile t)=>Math.Max(Math.Abs(t.X-goal.X),Math.Abs(t.Y-goal.Y))+Math.Abs(t.Plane-goal.Plane)*10;
        int expanded=0;
        while(queue.TryDequeue(out var tile,out _))
        {
            if((expanded++&1023)==0)ct.ThrowIfCancellationRequested();if(expanded>250000)return null;
            if(tile.Distance(goal)<=radius)
            {
                var path=new List<PathStep>();while(tile!=start){var p=parents[tile];path.Add(new(tile,p.Transport));tile=p.From;}
                path.Reverse();return path.ToArray();
            }
            var next=new List<(WorldTile Tile,MapTransport? Transport,int Cost)>();
            for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
            {
                var n=tile with{X=tile.X+dx,Y=tile.Y+dy};if((dx!=0||dy!=0)&&Move(tile,n))next.Add((n,null,Boundary(tile,n)?4:1));
            }
            next.AddRange(Transports.Where(t=>t.From==tile).Select(t=>(t.To,(MapTransport?)t,5)));
            foreach(var n in next)
            {
                int candidate=cost[tile]+n.Cost;
                if(cost.TryGetValue(n.Tile,out int old)&&old<=candidate)continue;
                cost[n.Tile]=candidate;parents[n.Tile]=(tile,n.Transport);queue.Enqueue(n.Tile,candidate+Heuristic(n.Tile));
            }
        }
        return null;
    }
    internal static readonly string[] MinimapHashes={
        "843CF9BFEDA3FC8D4B3B41552A483A9E2974012C96F734A32D623F366010C41A",
        "36811CC033C367CDF5A744F134DEFA533750DDCC61AEDE8D80903CCDFBFD5639",
        "480E6851E5D24FB43F782B885FE139208135447FD0D98D73503FE24904E96777",
        "D715DED5DBAB87C52AD8AC3F532B99CE00D5E3F156F0DAF2C79AE807D181F197"};
    internal static Cv.Mat DecodeMinimapReference(byte[] bytes,int plane)
    {
        if(plane is <0 or >3||Convert.ToHexString(SHA256.HashData(bytes))!=MinimapHashes[plane])
            throw new RoutineBlockedException("MINIMAP_REFERENCE_HASH_MISMATCH");
        var image=Cv.Cv2.ImDecode(bytes,Cv.ImreadModes.Grayscale);
        if(image.Width!=(MaxX-MinX)*4||image.Height!=(MaxY-MinY)*4)
        {image.Dispose();throw new RoutineBlockedException("MINIMAP_REFERENCE_BOUNDS_INVALID");}
        return image;
    }
    internal void LoadMinimapReferences()
    {
        if(MinimapReferences.Count!=0)throw new InvalidOperationException("Minimap references already loaded");
        try
        {
            var assembly=typeof(WorldMapData).Assembly;
            for(int plane=0;plane<4;plane++)
            {
                string name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith($".MinimapAssets.plane-{plane}.png",StringComparison.Ordinal));
                using var stream=assembly.GetManifestResourceStream(name)!;using var buffer=new MemoryStream();stream.CopyTo(buffer);
                var image=DecodeMinimapReference(buffer.ToArray(),plane);
                try{MinimapReferences.Add(new(plane,image));}catch{image.Dispose();throw;}
            }
        }
        catch{foreach(var r in MinimapReferences)r.Dispose();MinimapReferences.Clear();throw;}
    }
    public void Dispose()
    {foreach(var r in References)r.Dispose();References.Clear();foreach(var r in MinimapReferences)r.Dispose();MinimapReferences.Clear();}
}
