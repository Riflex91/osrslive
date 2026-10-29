namespace OSRSLiveDiagnostics;

internal static class WindowLayoutTests
{
    internal static int Run()
    {
        int result=1;
        using var window=new MainWindow(false,true);
        window.Shown+=(_,_)=>
        {
            try
            {
                window.VerifyDockedLayout();
                Console.WriteLine("WINDOW LAYOUT PASS: fixed 1000x600 viewport, 180px bottom log dock, no game resize on hide/show");
                result=0;
            }
            catch(Exception ex){Console.Error.WriteLine("WINDOW LAYOUT FAIL: "+ex);}
            finally{window.Close();}
        };
        Application.Run(window);
        if(result==0)
        {
            using var runeLite=new RuneLiteWindow();
            try
            {
                runeLite.VerifyDiagnosticLayout();
                Console.WriteLine("RUNELITE LOG LAYOUT PASS: neutral default banner, read-only diagnostic transcript, copy button");
            }
            catch(Exception ex){Console.Error.WriteLine("RUNELITE LOG LAYOUT FAIL: "+ex);result=1;}
        }
        return result;
    }
}
