namespace OSRSLiveDiagnostics;

internal static class DockedWindowLayout
{
    internal static readonly Size GameSize=new(1000,600);
    internal const int HeaderHeight=39,StatusHeight=56,ToolbarHeight=44,LogHeight=180,Inset=11;
    internal static Size ClientSize(bool logVisible)=>new(GameSize.Width+Inset*2,
        Inset*2+HeaderHeight+StatusHeight+GameSize.Height+ToolbarHeight+(logVisible?LogHeight:0));
}
