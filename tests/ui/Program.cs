// Actual compiled WinForms, Windows net48 only. The game's initializer is disabled;
// no Steam, installed game, native hooks or game process is used.
using System;
using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

static class LauncherUiTests
{
    const BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
    static int checks;
    static void Check(bool value,string name) {if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static object Get(object o,string name) => o.GetType().GetField(name,Instance).GetValue(o);
    static void Set(object o,string name,object value) => o.GetType().GetField(name,Instance).SetValue(o,value);
    static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Instance).Invoke(o,args);
    static void Layout(Form form) {form.PerformLayout();Application.DoEvents();form.PerformLayout();Application.DoEvents();}
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var assembly=Assembly.LoadFrom(args[0]);
            Type main=assembly.GetType("MPFever.MainForm"),language=assembly.GetType("MPFever.L"),roomType=assembly.GetType("MPFever.LobbyRoom"),viewType=assembly.GetType("MPFever.LobbyView");
            string version=(string)main.GetField("Version",Static).GetRawConstantValue();
            main.GetField("MenuModeDefault",Static).SetValue(null,true);
            language.GetMethod("Set",Static).Invoke(null,new object[]{"en"});
            // The second argument only suppresses Shown->Init. Normal production
            // startup always uses its default true.
            using(var form=(Form)Activator.CreateInstance(main,Instance,null,new object[]{false,false},null))
            {
                Set(form,"prelaunchLobby",true);Set(form,"lobbyContent",new string('a',64));
                Call(form,"InitLobbyControls");form.Show();Call(form,"UpdateLobbyUi");Layout(form);
                Check(Get(form,"menuGame")==null && Get(form,"hostGame")==null,"real lobby window opens with no GameLink/game process");
                Check(((Control)Get(form,"lobbyCard")).Visible,"prelaunch card is visible in actual WinForms");
                Check(!((Control)Get(form,"sessionControls")).Visible,"game speed controls hidden before launch");
                Check(!((Button)Get(form,"lobbyLaunch")).Enabled && !((Button)Get(form,"lobbyReady")).Enabled,"unconnected window cannot launch or ready");
                var room=Activator.CreateInstance(roomType,Instance,null,new object[]{version,new string('a',64),"save","separate"},null);
                Call(room,"Join","guest");Set(form,"hostLobby",room);Set(form,"running",true);
                Call(form,"UpdateLobbyUi");Layout(form);
                Check(!((Button)Get(form,"lobbyLaunch")).Enabled,"actual host button blocks an unready client");
                string id=(string)roomType.GetField("Id",Instance).GetValue(room);int revision=(int)roomType.GetProperty("Revision",Instance).GetValue(room);
                Call(room,"Ready","guest",id,revision,version,new string('a',64),true);
                object view=viewType.GetMethod("Parse",Static).Invoke(null,new[]{Call(room,"Payload")});Set(form,"lobbyView",view);
                Call(form,"UpdateLobbyUi");Layout(form);
                Check(((Button)Get(form,"lobbyLaunch")).Enabled,"actual host button enables after authoritative readiness");
                Check(!((Control)Get(form,"lobbySave")).Enabled,"host cannot silently change save after creating room");
                foreach(string code in new[]{"en","ru"})
                {
                    language.GetMethod("Set",Static).Invoke(null,new object[]{code});Layout(form);Call(form,"UpdateLobbyUi");Layout(form);
                    foreach(string name in new[]{"lobbyReady","lobbyLaunch","lobbyCancel"})
                    {
                        var button=(Control)Get(form,name);
                        Check(!button.IsDisposed && button.Visible,"language rebuild preserves visible "+code+"/"+name);
                        Check(button.Left>=0 && button.Right<=button.Parent.ClientSize.Width && button.Bottom<=button.Parent.ClientSize.Height,"lobby action fits its real container "+code+"/"+name);
                    }
                }
                Call(room,"Leave","guest");Call(room,"Join","guest");Call(form,"UpdateLobbyUi");
                Check(!((Button)Get(form,"lobbyLaunch")).Enabled,"actual window disables start after reconnect invalidates ready");
                ((Button)Get(form,"lobbyCancel")).PerformClick();Call(form,"UpdateLobbyUi");Layout(form);
                Check(Get(form,"hostLobby")==null && ((Button)Get(form,"hostBtn")).Enabled,"actual cancel button resets room and enables hosting again");
                Check(Get(form,"menuGame")==null,"lobby controls do not create a game process");
                // Exercise the actual queued callbacks, not a reimplementation.
                // A null gameDir guarantees any attempted launch fails before
                // Process.Start; lobbyLaunchFailed reveals such an attempt.
                var link=Activator.CreateInstance(assembly.GetType("MPFever.GameLink"),Instance,null,new object[]{"ui-lifetime","menu"},null);
                Set(form,"menuGame",link);((IList)Get(form,"games")).Add(link);Set(form,"gameDir",null);
                Call(form,"QueueLobbyLaunch",new object[]{null,null});
                Call(form,"EndSession",new object[]{null});Layout(form);
                Check(!(bool)Get(form,"lobbyLaunchFailed"),"actual queued launch is discarded after session cancellation");
                Call(form,"QueueLobbyLaunch",new object[]{null,(Func<bool>)(()=>false)});Layout(form);
                Check(!(bool)Get(form,"lobbyLaunchFailed"),"actual queued launch is discarded for disconnected client in same session");
                Call(form,"QueueLobbyLaunch",new object[]{"different-room",null});Layout(form);
                Check(!(bool)Get(form,"lobbyLaunchFailed"),"actual queued launch is bound to current lobby room");
                Call(form,"QueueLobbyLaunch",new object[]{null,(Func<bool>)(()=>true)});Layout(form);
                Check((bool)Get(form,"lobbyLaunchFailed") && !(bool)Get(form,"lobbyGameLaunched"),"current launch reaches guarded path and missing executable fails closed");
                Check(((Button)Get(form,"lobbyLaunch")).Enabled,"failed process start offers explicit retry in actual window");
                Check(!(bool)link.GetType().GetProperty("GameRunning",Instance).GetValue(link),"queued launch tests never started a game process");
                form.Close();
            }
            Console.WriteLine("Windows WinForms lobby: "+checks+" checks passed; game initialization disabled, no game was run.");return 0;
        }
        catch(Exception e) {Console.Error.WriteLine(e);return 1;}
    }
}
