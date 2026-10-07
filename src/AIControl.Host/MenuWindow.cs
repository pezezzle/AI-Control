using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace AIControl;

public partial class MainWindow
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    private void ApplyDarkTitleBar()
    {
        var window=new WindowInteropHelper(this).Handle;int enabled=1,background=0x181818,foreground=0xededed;
        if(DwmSetWindowAttribute(window,20,ref enabled,4)!=0)DwmSetWindowAttribute(window,19,ref enabled,4);
        DwmSetWindowAttribute(window,35,ref background,4);DwmSetWindowAttribute(window,36,ref foreground,4);
    }
    private void Menu_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem {Tag:string action})return;
        e.Handled=true;
        try
        {
            if(action=="paste"){if(Clipboard.ContainsText())Send(new{type="menuAction",value=action,text=Clipboard.GetText()});Shell.Focus();return;}
            if(action=="exit"){Close();return;}
            if(action=="openWorkspaceFolder"){OpenPath(CurrentChat.Data.Workspace);return;}
            if(action=="openEditor"){OpenEditor();return;}
            if(action is "openBackups" or "openDataFolder")
            {var folder=action=="openBackups"?WriteBackups.DirectoryPath:SettingsStore.DataDirectory;Directory.CreateDirectory(folder);OpenPath(folder);return;}
            if(action is "overview" or "comfy" or "coding" or "chat" or "settings" or "newChat" or "chooseWorkspace" or "helpGuide" or "helpKeys" or "helpAbout")HideWorkspace();
            Send(new{type="menuAction",value=action});
            Shell.Focus();
        }
        catch(Exception ex){notice=FriendlyError(ex);SendSnapshot();}
    }
    private void ApplyViewPreferences(bool sidebar,bool context,double zoom)
    {
        if(!double.IsFinite(zoom)||zoom is <.75 or >1.5)throw new InvalidDataException("Choose a zoom from 75% to 150%.");
        SidebarMenu.IsChecked=sidebar;ContextMenuItem.IsChecked=context;
        Shell.ZoomFactor=zoom;ServiceView.ZoomFactor=zoom;
    }
}
