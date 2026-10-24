using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal sealed record LiveItemQuote(int ItemId,string Name,long Price,DateTimeOffset TransactionUtc,int BuyLimit);
internal sealed class WikiPriceService:IDisposable
{
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(12),BaseAddress=new Uri("https://prices.runescape.wiki/api/v1/osrs/")};
    private readonly Dictionary<string,(int Id,int Limit)> mapping=new(StringComparer.OrdinalIgnoreCase);
    internal WikiPriceService()=>http.DefaultRequestHeaders.UserAgent.ParseAdd("OSRS-Play/1.6.0 (+https://github.com/Riflex91/osrslive; bounded-supply-pricing)");
    internal async Task<LiveItemQuote?> Get(string item,bool sell,CancellationToken ct)
    {
        if(mapping.Count==0)
        {
            using var response=await http.GetAsync("mapping",ct);response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach(var entry in json.RootElement.EnumerateArray())
                if(!entry.GetProperty("members").GetBoolean()&&entry.TryGetProperty("limit",out var limit)&&limit.ValueKind==JsonValueKind.Number)
                    mapping[entry.GetProperty("name").GetString()!]=(entry.GetProperty("id").GetInt32(),limit.GetInt32());
        }
        if(!mapping.TryGetValue(item,out var mapped))return null;
        using var prices=await http.GetAsync("latest?id="+mapped.Id,ct);prices.EnsureSuccessStatusCode();
        using var latest=JsonDocument.Parse(await prices.Content.ReadAsStringAsync(ct));
        if(!latest.RootElement.GetProperty("data").TryGetProperty(mapped.Id.ToString(),out var value))return null;
        string key=sell?"low":"high";
        if(!value.TryGetProperty(key,out var price)||price.ValueKind!=JsonValueKind.Number||
            !value.TryGetProperty(key+"Time",out var time)||time.ValueKind!=JsonValueKind.Number)return null;
        var timestamp=DateTimeOffset.FromUnixTimeSeconds(time.GetInt64());
        if(timestamp>DateTimeOffset.UtcNow||DateTimeOffset.UtcNow-timestamp>TimeSpan.FromSeconds(60))return null;
        return new(mapped.Id,item,price.GetInt64(),timestamp,mapped.Limit);
    }
    public void Dispose()=>http.Dispose();
}
