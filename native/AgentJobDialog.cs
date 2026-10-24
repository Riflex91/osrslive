using System.Text.Json;

namespace OSRSLiveDiagnostics;

internal sealed class AgentJobDialog:Form
{
    internal AgentJobDialog()
    {
        Text="Agentenaufgabe";ClientSize=new(520,310);FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
        var kinds=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        kinds.Items.AddRange(new object[]{"Training","Herstellen","Kaufen","Verkaufen","Bank: ablegen","Bank: abheben"});kinds.SelectedIndex=0;
        var target=new ComboBox{Dock=DockStyle.Fill};
        var amount=new NumericUpDown{Minimum=1,Maximum=1000,Value=1,Dock=DockStyle.Fill};
        var mode=new CheckBox{Text="Ironman-Konto",AutoSize=true};
        var reserve=new NumericUpDown{Minimum=0,Maximum=1000000,Value=500,Dock=DockStyle.Fill};
        var detail=new Label{AutoSize=false,Dock=DockStyle.Fill,Text="Die Automatik wählt ohne Einzelauftrag selbst eine versorgte Aktivität. Einzelaufträge benötigen Gegenstände und Ziel in der sichtbaren Spielumgebung."};
        var start=new Button{Text="Aufgabe einreihen",AutoSize=true};
        var discard=new Button{Text="Auftrag verwerfen",AutoSize=true};
        var feedback=new Label{Dock=DockStyle.Fill,AutoSize=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=2,RowCount=8};
        layout.ColumnStyles.Add(new(SizeType.Absolute,130));layout.ColumnStyles.Add(new(SizeType.Percent,100));
        for(int i=0;i<5;i++)layout.RowStyles.Add(new(SizeType.Absolute,30));
        layout.RowStyles.Add(new(SizeType.Absolute,60));layout.RowStyles.Add(new(SizeType.Absolute,32));layout.RowStyles.Add(new(SizeType.Percent,100));
        void Add(string text,Control control,int row){layout.Controls.Add(new Label{Text=text,Dock=DockStyle.Fill},0,row);layout.Controls.Add(control,1,row);}
        Add("Aufgabe",kinds,0);Add("Ziel / Gegenstand",target,1);Add("Menge",amount,2);Add("Münzreserve",reserve,3);Add("Kontotyp",mode,4);
        layout.Controls.Add(detail,0,5);layout.SetColumnSpan(detail,2);layout.Controls.Add(start,1,6);
        layout.Controls.Add(discard,0,6);
        layout.Controls.Add(feedback,0,7);layout.SetColumnSpan(feedback,2);Controls.Add(layout);
        void RefreshTargets()
        {
            amount.Value=1;
            amount.Maximum=kinds.SelectedIndex is 0 or 1 or 4?1:kinds.SelectedIndex==5?28:1000;
            target.Items.Clear();target.Text="";
            if(kinds.SelectedIndex==0)target.Items.AddRange(ExecutionCatalog.Training.Select(s=>(object)s.Id).ToArray());
            else if(kinds.SelectedIndex==1)target.Items.AddRange(F2PProgression.Recipes.Select(s=>(object)s.Product).ToArray());
            else target.Items.AddRange(new object[]{"iron axe","iron pickaxe","copper ore","tin ore","leather","thread","logs","bronze bar"});
            target.SelectedIndex=0;
        }
        kinds.SelectedIndexChanged+=(_,_)=>RefreshTargets();RefreshTargets();
        string agentDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","agent");
        string settingsPath=Path.Combine(agentDirectory,"settings.json");
        if(File.Exists(settingsPath))
        {
            try
            {
                var saved=JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(settingsPath));
                if(saved is not null){mode.Checked=saved.Ironman;reserve.Value=Math.Clamp(saved.ReserveCoins,0,1000000);}
            }
            catch{feedback.Text="Gespeicherte Einstellungen konnten nicht gelesen werden.";}
        }
        discard.Click+=(_,_)=>
        {
            try
            {
                File.Delete(Path.Combine(agentDirectory,"job.json"));
                feedback.Text="Einzelauftrag verworfen. GE-Angebote bleiben bestehen. Automatik ist pausiert.";
            }
            catch(Exception ex){feedback.Text="Auftrag konnte nicht entfernt werden: "+ex.GetType().Name;}
        };
        start.Click+=(_,_)=>
        {
            string value=target.Text.Trim();
            if(value.Length is <1 or >50){feedback.Text="Bitte ein gültiges Ziel eingeben.";return;}
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OSRSPlay","agent");
            try
            {
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"job.json");
                if(File.Exists(path)){feedback.Text="Eine Aufgabe ist bereits eingereiht; der Log zeigt ihren Status.";return;}
                string kind=new[]{"train","craft","buy","sell","deposit","withdraw"}[kinds.SelectedIndex];
                var settings=new AgentSettings(mode.Checked,(long)reserve.Value);
                File.WriteAllText(Path.Combine(directory,"settings.json"),JsonSerializer.Serialize(settings));
                string temp=path+".tmp";
                File.WriteAllText(temp,JsonSerializer.Serialize(new AgentJob(Guid.NewGuid().ToString("N"),kind,value,(int)amount.Value)));
                File.Move(temp,path,false);
                DialogResult=DialogResult.OK;Close();
            }
            catch(Exception ex){feedback.Text="Aufgabe konnte nicht gespeichert werden: "+ex.GetType().Name;}
        };
    }
}
